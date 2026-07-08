using UnityEngine;
using UnityEngine.InputSystem;

namespace StarterAssets
{
    /// <summary>
    /// Lightweight compatibility layer for packages that expect the
    /// official Starter Assets input component to exist in the project.
    /// It exposes the common Starter Assets fields and the extra superhero
    /// actions required by the Forge Horizon add-on.
    /// </summary>
    public class StarterAssetsInputs : MonoBehaviour
    {
        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;

        [Header("Forge Horizon Add-on Input Values")]
        public bool aim;
        public bool lockon;
        public bool punch;
        public bool fly;
        public bool ascend;
        public bool descend;
        public bool carry;
        public bool flash;
        public bool fire;

        public void MoveInput(Vector2 newMoveDirection)
        {
            move = newMoveDirection;
        }

        public void LookInput(Vector2 newLookDirection)
        {
            if (cursorInputForLook)
            {
                look = newLookDirection;
            }
        }

        public void JumpInput(bool newJumpState)
        {
            jump = newJumpState;
        }

        public void SprintInput(bool newSprintState)
        {
            sprint = newSprintState;
        }

        public void AimInput(bool newAimState)
        {
            aim = newAimState;
        }

        public void LockOnInput(bool newLockOnState)
        {
            lockon = newLockOnState;
        }

        public void PunchInput(bool newPunchState)
        {
            punch = newPunchState;
        }

        public void FlyInput(bool newFlyState)
        {
            fly = newFlyState;
        }

        public void AscendInput(bool newAscendState)
        {
            ascend = newAscendState;
        }

        public void DescendInput(bool newDescendState)
        {
            descend = newDescendState;
        }

        public void CarryInput(bool newCarryState)
        {
            carry = newCarryState;
        }

        public void FlashInput(bool newFlashState)
        {
            flash = newFlashState;
        }

        public void FireInput(bool newFireState)
        {
            fire = newFireState;
        }

        public void OnMove(InputValue value)
        {
            MoveInput(value.Get<Vector2>());
        }

        public void OnLook(InputValue value)
        {
            if (cursorInputForLook)
            {
                LookInput(value.Get<Vector2>());
            }
        }

        public void OnJump(InputValue value)
        {
            JumpInput(value.isPressed);
        }

        public void OnSprint(InputValue value)
        {
            SprintInput(value.isPressed);
        }

        public void OnAim(InputValue value)
        {
            AimInput(value.isPressed);
        }

        public void OnLockOn(InputValue value)
        {
            LockOnInput(value.isPressed);
        }

        public void OnPunch(InputValue value)
        {
            PunchInput(value.isPressed);
        }

        public void OnFly(InputValue value)
        {
            FlyInput(value.isPressed);
        }

        public void OnAscend(InputValue value)
        {
            AscendInput(value.isPressed);
        }

        public void OnDescend(InputValue value)
        {
            DescendInput(value.isPressed);
        }

        public void OnCarry(InputValue value)
        {
            CarryInput(value.isPressed);
        }

        public void OnFlash(InputValue value)
        {
            FlashInput(value.isPressed);
        }

        public void OnFire(InputValue value)
        {
            FireInput(value.isPressed);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetCursorState(cursorLocked && hasFocus);
        }

        public void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
