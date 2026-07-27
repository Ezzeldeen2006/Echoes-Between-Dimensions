using System.Collections;
using UnityEngine;
using UnityEngine.AI;
public class robot : MonoBehaviour
{
    private enum RobotState { Sleeping, Patrolling, Chasing, Attacking }
    private RobotState _state = RobotState.Sleeping;

    public Transform player;
    public Transform[] patrolPoints;
    public GameObject laserPrefab;
    public Transform firePoint;

    private float detectionRange = 12f;
    private float attackRange = 6f;
    private float patrolWaitTime = 2f;
    private float losePlayerTime = 10f;
    private float patrolTimeLimit = 10f;
    private float stopAtDistance = 1f;
    //ai generated
    private NavMeshAgent _agent;
    private Animator _anim;
    //ai generated
    private int _currentPatrolIndex;
    private bool _isWaiting;
    private float _timeSinceLostPlayer;
    private float _patrolTimer;
    private float _fireTimer;

    // Ranges pre-squared once, so the per-frame distance check never needs a square root.
    // See Update() for why that is exact rather than an approximation.
    private float _detectionRangeSqr;
    private float _attackRangeSqr;

    // transform is a native property call, not a field. This script touches it several times
    // a frame, per robot, forever.
    private Transform _tf;

    // Roughly chest height on the player capsule. Where a line-of-sight test should aim: at
    // the feet it is blocked by every step, at the head it clears cover the player is behind.
    private const float PlayerChestHeight = 1.2f;

    // Set when a patrol destination is issued, cleared once the agent has actually taken the
    // path. See Patrol() for the bug this exists to prevent.
    private bool _awaitingPatrolPath;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim = GetComponent<Animator>();
        _tf = transform;

        _detectionRangeSqr = detectionRange * detectionRange;
        _attackRangeSqr = attackRange * attackRange;
    }

    private void Start()
    {
        EnsureAgentOnNavMesh();
        EnterSleep();
    }

    /// <summary>
    /// Re-attaches the agent to the NavMesh if it failed to attach during scene load.
    ///
    /// <para><b>This is the actual "NavMesh bug".</b> The WebGL build logs
    /// <i>"Failed to create agent because there is no valid NavMesh"</i> once per robot, and
    /// an agent that fails that check never retries -- so every enemy in the build stands
    /// still forever. The scene is fine: the NavMesh Surface object is active, its component
    /// is enabled, and it references its baked data.</para>
    ///
    /// <para>It is an ORDERING problem. A NavMeshAgent attaches to the mesh in its own
    /// <c>OnEnable</c>, and NavMeshSurface registers its baked data in <c>OnEnable</c> too.
    /// Unity does not order those against each other, so whether the enemies work depends on
    /// which component the scene loader happens to reach first -- which is why this can look
    /// fine in the editor and be broken in a build, from identical files.</para>
    ///
    /// <para><c>Start</c> is the fix's leverage: Unity guarantees every <c>Awake</c> and
    /// <c>OnEnable</c> in the scene has run before any <c>Start</c> does. So by the time this
    /// runs the surface has definitely registered, and toggling the agent re-runs the attach
    /// that failed. Cheap, and it does not depend on execution-order settings that live
    /// outside version control.</para>
    /// </summary>
    private void EnsureAgentOnNavMesh()
    {
        if (_agent == null || !_agent.enabled) return;
        if (_agent.isOnNavMesh) return;

        // Toggling re-runs OnEnable, and with it the attach.
        _agent.enabled = false;
        _agent.enabled = true;

        if (_agent.isOnNavMesh)
        {
            Debug.Log($"[robot] '{name}' attached to the NavMesh on retry -- the surface " +
                      "had not registered its data when the agent first enabled.", this);
            return;
        }

        /*
         * Still not attached, so this is not a timing problem for this robot: it is standing
         * somewhere the mesh does not cover. Snap it to the nearest point that is covered
         * rather than leaving it inert.
         *
         * 5 units of search radius, not more. A robot that has to be dragged further than that
         * was placed somewhere genuinely wrong, and silently teleporting it across the level
         * would hide a level-design mistake instead of reporting one.
         */
        if (NavMesh.SamplePosition(_tf.position, out NavMeshHit hit, 5f, NavMesh.AllAreas)
            && _agent.Warp(hit.position))
        {
            Debug.LogWarning($"[robot] '{name}' was off the NavMesh and has been moved " +
                             $"{Vector3.Distance(_tf.position, hit.position):0.0} units onto it.", this);
            return;
        }

        /*
         * LogError, not LogWarning, and that is a deliberate severity choice rather than
         * shouting.
         *
         * Reaching here means this enemy will never move for the whole session: the retry
         * failed AND there is no NavMesh within 5 units to snap to. A robot that cannot
         * path is not a degraded robot, it is a missing one, and the level is built around
         * them chasing you. That is worth an error.
         *
         * It is also the only diagnostic that survives. A release WebGL build does not
         * surface Debug.Log to the browser console the way the editor does -- the engine's
         * own errors come through, which is how the "no valid NavMesh" lines were visible at
         * all, but an ordinary Log may not. So an error here is the one signal that can
         * distinguish "the retry fixed it" from "the retry did nothing", from outside the
         * game, without a development build.
         */
        Debug.LogError($"[robot] '{name}' could not be attached to a NavMesh and will never move. " +
                       "The surface's baked data did not load in this build.", this);
    }

    private void Update()
    {
        if (player == null)
            return;

        /*
         * Squared distance, not Vector3.Distance.
         *
         * Distance() takes a square root, and every use of it here is a COMPARISON against a
         * fixed range. Comparing squared values gives an identical answer for every input --
         * squaring is monotonic for non-negative numbers, and distances are never negative --
         * so this is not an approximation, it is the same test without the sqrt. One robot
         * saves one square root per frame; a room of them saves one each, every frame, for
         * the whole session.
         */
        float distSqr = (player.position - _tf.position).sqrMagnitude;
        _fireTimer += Time.deltaTime;
        //ai generated
        switch (_state)
        {
            case RobotState.Sleeping:
                if (distSqr <= _detectionRangeSqr && CanSeePlayer())
                    EnterWake();
                break;

            case RobotState.Patrolling:
                Patrol();
                _patrolTimer += Time.deltaTime;
                if (_patrolTimer >= patrolTimeLimit)
                    EnterSleep();
                if (distSqr <= _detectionRangeSqr && CanSeePlayer())
                    EnterWake();
                break;

            case RobotState.Chasing:
                FollowPlayer();
                if (distSqr <= _attackRangeSqr)
                {
                    EnterAttack();
                }
                else if (!CanSeePlayer())
                {
                    _timeSinceLostPlayer += Time.deltaTime;
                    if (_timeSinceLostPlayer >= losePlayerTime)
                        EnterPatrol();
                }
                else
                {
                    _timeSinceLostPlayer = 0f;
                }
                break;

            case RobotState.Attacking:
                Attack();
                if (distSqr > _attackRangeSqr)
                    EnterChase();
                break;
        }

        UpdateAnimations();
    }
    //ai generated
    private void EnterSleep()
    {
        // Same reason as EnterAttack: a wake timer left running would wake a robot that has
        // just gone back to sleep, with no player anywhere near it.
        CancelInvoke(nameof(EnterChase));

        _state = RobotState.Sleeping;
        SetAgentStopped(true);
        _anim.SetBool("IsAwake", false);
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", false);
        _anim.SetTrigger("Close");
    }
    //ai generated
    private void EnterWake()
    {
        _state = RobotState.Chasing;
        SetAgentStopped(true);
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", false);
        _anim.SetBool("IsAwake", true);
        // nameof rather than the string literal "EnterChase". Identical at runtime -- Invoke
        // still resolves by name -- but the compiler now checks it. The string version fails
        // SILENTLY if the method is ever renamed: the robot wakes, plays its wake animation,
        // and then simply never starts chasing, with nothing in the console to say why.
        Invoke(nameof(EnterChase), 1.2f);
    }
    //ai generated
    private void EnterChase()
    {
        _state = RobotState.Chasing;
        SetAgentStopped(false);
        _timeSinceLostPlayer = 0f;
        _anim.SetBool("IsAttacking", false);
        _anim.SetBool("IsChasing", true);
    }
    //ai generated
    private void EnterAttack()
    {
        /*
         * Cancel the wake timer.
         *
         * EnterWake schedules EnterChase 1.2s out, to hold the robot still while its opening
         * animation plays. If the player closes to attack range inside that window -- which is
         * exactly what happens when you wake a robot at point-blank range -- the robot enters
         * Attacking, and then the timer fires anyway and drags it back to Chasing. Update
         * immediately sees it is still in range and calls EnterAttack again, so the attack
         * animation restarts from frame one and the first shot is thrown away.
         *
         * It reads as the robot flinching, and only when you surprise one up close, which is
         * the hardest kind of bug to reproduce on purpose.
         */
        CancelInvoke(nameof(EnterChase));

        _state = RobotState.Attacking;
        SetAgentStopped(true);
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", true);
    }
    //ai generated
    private void EnterPatrol()
    {
        _state = RobotState.Patrolling;
        SetAgentStopped(false);
        _isWaiting = false;
        _patrolTimer = 0f;
        _timeSinceLostPlayer = 0f;
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", false);
        GoToNextPatrolPoint();
    }
    //ai generated
    private void Patrol()
    {
        if (_isWaiting) return;
        if (_agent.pathPending) return;

        /*
         * <b>The patrol-point skip.</b>
         *
         * remainingDistance is 0 for the frames between SetDestination and the agent actually
         * holding the new path -- pathPending covers the calculation, but not the frame after
         * it completes and before the path is adopted. So "have I arrived?" answered YES
         * immediately after being told where to go, and the robot walked its entire patrol
         * route standing still: point 1 reached, wait, point 2 reached, wait, point 3 reached.
         * From the player's side a patrolling robot simply never patrols.
         *
         * hasPath is the missing half. Once a real path exists the distance means something;
         * until then this waits. The velocity term catches the other end -- an agent that has
         * consumed its path and stopped has hasPath false again, and without it a robot that
         * genuinely arrived would never trigger the wait.
         */
        if (_awaitingPatrolPath)
        {
            if (!_agent.hasPath) return;
            _awaitingPatrolPath = false;
        }

        bool arrived = _agent.remainingDistance <= stopAtDistance
                       && (!_agent.hasPath || _agent.velocity.sqrMagnitude < 0.01f);
        if (arrived)
            StartCoroutine(WaitAtPatrolPoint());
    }
    //ai generated
    private IEnumerator WaitAtPatrolPoint()
    {
        _isWaiting = true;
        SetAgentStopped(true);
        _anim.SetBool("IdleF", true);

        yield return new WaitForSeconds(patrolWaitTime);

        if (_state != RobotState.Patrolling)
        {
            _isWaiting = false;
            yield break;
        }

        _anim.SetBool("IdleF", false);
        SetAgentStopped(false);
        GoToNextPatrolPoint();
        _isWaiting = false;
    }
    //ai generated
    private void GoToNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (!CanNavigate()) return;

        _agent.SetDestination(patrolPoints[_currentPatrolIndex].position);
        _awaitingPatrolPath = true;
        _currentPatrolIndex = (_currentPatrolIndex + 1) % patrolPoints.Length;
    }
    //ai generated
    private void FollowPlayer()
    {
        if (!CanNavigate()) return;
        _agent.SetDestination(player.position);
    }

    /// <summary>
    /// Starts and stops the agent, safely.
    ///
    /// <para>Writing <c>isStopped</c> on an agent that is not on the NavMesh throws the same
    /// per-frame error as SetDestination. Start() calls EnterSleep on the first frame, before
    /// anything has had a chance to notice the agent is misplaced, so that path is the most
    /// likely one of all to hit it -- one badly-placed robot, and the console is unusable from
    /// the moment you press play.</para>
    /// </summary>
    private void SetAgentStopped(bool stopped)
    {
        if (!CanNavigate()) return;
        _agent.isStopped = stopped;
    }

    /// <summary>
    /// Whether this agent can be given a destination at all.
    ///
    /// <para>SetDestination on an agent that is not on the NavMesh does not fail quietly -- it
    /// logs an error <b>every frame</b>, from every affected robot. A handful of them off the
    /// mesh fills the console faster than anything else can be read, which is how a single
    /// misplaced robot turns into "the NavMesh is broken": the real errors are still there,
    /// several thousand lines up.</para>
    ///
    /// <para>An agent lands off the mesh easily -- placed slightly inside geometry, or on a
    /// part of the level that was not baked. Checking is one property read, and it converts a
    /// flood into a single line naming the object.</para>
    /// </summary>
    private bool CanNavigate()
    {
        if (_agent == null || !_agent.isActiveAndEnabled) return false;
        if (_agent.isOnNavMesh) return true;

        if (!_warnedOffMesh)
        {
            _warnedOffMesh = true;
            Debug.LogWarning($"[robot] '{name}' is not on the NavMesh, so it cannot move. " +
                             "Check it is inside the baked area and above the ground.", this);
        }
        return false;
    }

    private bool _warnedOffMesh;
    //ai generated
    private void Attack()
    {
        Vector3 dir = player.position - _tf.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            _tf.rotation = Quaternion.Slerp(_tf.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);

        if (_fireTimer >= 1f)
        {
            FireLaser();
            _fireTimer = 0f;
        }
    }

    private void FireLaser()
    {
        if (laserPrefab == null || firePoint == null) return;
        Vector3 aimDir = (player.position - firePoint.position).normalized;
        GameObject laser = Instantiate(laserPrefab, firePoint.position + Vector3.up *0.2f, Quaternion.LookRotation(aimDir));
        Destroy(laser, 4f);
    }
    //ai generated
    private void UpdateAnimations()
    {
        bool isMoving = _agent.velocity.sqrMagnitude > 0.01f;
        _anim.SetBool("Walking", isMoving);
    } 
    //ai generated
    private bool CanSeePlayer()
    {
        /*
         * Cast from EYE height at both ends, not from the floor.
         *
         * Both transforms sit at ground level, so the original cast a ray along the ground
         * from the robot's feet to the player's feet. Every kerb, dock plank, step and slope
         * on the level is then a sight blocker: the robot loses the player while looking
         * straight at them, waits out losePlayerTime, and wanders off to patrol. That is most
         * of "the robots react oddly" -- the state machine was working perfectly on a
         * line-of-sight test that was wrong.
         *
         * The agent's own height is used for the robot end rather than a hardcoded number, so
         * this stays correct if the robot is ever rescaled. The player end is a constant,
         * because a CharacterController's position is its centre-bottom and chest height is
         * what a shooter should be sighting on.
         */
        float robotEye = _agent != null ? _agent.height * 0.65f : 1f;
        Vector3 origin = _tf.position + Vector3.up * robotEye;
        Vector3 dirToPlayer = (player.position + Vector3.up * PlayerChestHeight) - origin;

        /*
         * One square root instead of two.
         *
         * `.normalized` computes the magnitude and divides by it; `.magnitude` then computes
         * it a second time. Taking the length once and dividing by hand gives exactly the
         * same direction and the same distance, at half the cost -- and this is called from
         * Update in three of the four states.
         *
         * The zero-length guard is not paranoia: `.normalized` returns a zero vector for a
         * zero-length input rather than dividing by zero, so the original silently cast a ray
         * with no direction if the robot and player ever occupied the same point. Handled
         * explicitly here -- standing inside the robot obviously counts as seeing it.
         */
        float distance = dirToPlayer.magnitude;
        if (distance < 0.0001f)
            return true;

        Vector3 direction = dirToPlayer / distance;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
            return hit.transform == player;
        return true;
    }
}