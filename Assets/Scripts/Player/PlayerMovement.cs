using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    
    private Rigidbody2D _rb;
    private Vector2 _moveInput;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        
        // Rigidbody2D 기본 설정 강제 (실수 방지)
        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;
    }

    private void Update()
    {
        // Input System을 통한 이동 입력 (간이 구현)
        float moveX = 0;
        float moveY = 0;

        if (Keyboard.current.wKey.isPressed) moveY = 1;
        if (Keyboard.current.sKey.isPressed) moveY = -1;
        if (Keyboard.current.aKey.isPressed) moveX = -1;
        if (Keyboard.current.dKey.isPressed) moveX = 1;

        _moveInput = new Vector2(moveX, moveY).normalized;
    }

    private void FixedUpdate()
    {
        // 물리 기반 이동
        _rb.linearVelocity = _moveInput * moveSpeed;
    }
}
