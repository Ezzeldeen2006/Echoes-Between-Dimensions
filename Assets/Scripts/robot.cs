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

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim = GetComponent<Animator>();
    }

    private void Start()
    {
        EnterSleep();
    }

    private void Update()
    {
        if (player == null)
            return;
        float dist = Vector3.Distance(player.position, transform.position);
        _fireTimer += Time.deltaTime;
        //ai generated
        switch (_state)
        {
            case RobotState.Sleeping:
                if (dist <= detectionRange && CanSeePlayer())
                    EnterWake();
                break;

            case RobotState.Patrolling:
                Patrol();
                _patrolTimer += Time.deltaTime;
                if (_patrolTimer >= patrolTimeLimit)
                    EnterSleep();
                if (dist <= detectionRange && CanSeePlayer())
                    EnterWake();
                break;

            case RobotState.Chasing:
                FollowPlayer();
                if (dist <= attackRange)
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
                if (dist > attackRange)
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
        Invoke("EnterChase", 1.2f);
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
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);

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
        Vector3 dirToPlayer = player.position - transform.position;
        if (Physics.Raycast(transform.position, dirToPlayer.normalized, out RaycastHit hit, dirToPlayer.magnitude))
            return hit.transform == player;
        return true;
    }
}