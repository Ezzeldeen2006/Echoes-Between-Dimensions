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
        EnterSleep();
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
        _state = RobotState.Sleeping;
        _agent.isStopped = true;
        _anim.SetBool("IsAwake", false);
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", false);
        _anim.SetTrigger("Close");
    }
    //ai generated
    private void EnterWake()
    {
        _state = RobotState.Chasing;
        _agent.isStopped = true;
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
        _agent.isStopped = false;
        _timeSinceLostPlayer = 0f;
        _anim.SetBool("IsAttacking", false);
        _anim.SetBool("IsChasing", true);
    }
    //ai generated
    private void EnterAttack()
    {
        _state = RobotState.Attacking;
        _agent.isStopped = true;
        _anim.SetBool("IsChasing", false);
        _anim.SetBool("IsAttacking", true);
    }
    //ai generated
    private void EnterPatrol()
    {
        _state = RobotState.Patrolling;
        _agent.isStopped = false;
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
        if (_agent.remainingDistance <= stopAtDistance)
            StartCoroutine(WaitAtPatrolPoint());
    }
    //ai generated
    private IEnumerator WaitAtPatrolPoint()
    {
        _isWaiting = true;
        _agent.isStopped = true;
        _anim.SetBool("IdleF", true);

        yield return new WaitForSeconds(patrolWaitTime);

        if (_state != RobotState.Patrolling)
        {
            _isWaiting = false;
            yield break;
        }

        _anim.SetBool("IdleF", false);
        _agent.isStopped = false;
        GoToNextPatrolPoint();
        _isWaiting = false;
    }
    //ai generated
    private void GoToNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;
        _agent.SetDestination(patrolPoints[_currentPatrolIndex].position);
        _currentPatrolIndex = (_currentPatrolIndex + 1) % patrolPoints.Length;
    }
    //ai generated
    private void FollowPlayer()
    {
        _agent.SetDestination(player.position);
    }
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
        Vector3 origin = _tf.position;
        Vector3 dirToPlayer = player.position - origin;

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