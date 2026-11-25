using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

public static class PlayerControlPlayModeCheck
{
    // Use the live prefab's PlayerInput events. Do not subscribe the test directly to gameplay callbacks.
    public static IEnumerator Run(Camera camera)
    {
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var input = player.GetComponent<PlayerInput>();
        var melee = player.GetComponent<MeleeCombatController>();
        var dodge = player.GetComponent<MeleeDodgeController>();
        var body = player.GetComponent<Rigidbody2D>();
        Require(input.currentActionMap?.name == "Player", "player action map is enabled");
        var previousSettings = InputSystem.settings;
        var settings = UnityEngine.Object.Instantiate(previousSettings);
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings = settings;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        if (input.user.valid)
        {
            InputUser.PerformPairingWithDevice(keyboard, input.user);
            InputUser.PerformPairingWithDevice(mouse, input.user);
        }

        var swings = new List<int>();
        bool swingDuringDodge = false;
        Vector2 lastSwingPosition = Vector2.zero;
        Vector2 lastSwingVisualPosition = Vector2.zero;
        Vector2 lastSwingVelocity = Vector2.zero;
        void OnSwing(int index)
        {
            swings.Add(index);
            swingDuringDodge |= player.IsDashing;
            lastSwingPosition = body.position;
            lastSwingVisualPosition = player.transform.position;
            lastSwingVelocity = body.linearVelocity;
        }
        melee.OnAttackExecuted += OnSwing;
        Vector2 pointer = new(Screen.width / 2f, Screen.height / 2f);
        void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
        void Left(bool pressed)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left, pressed));
            InputSystem.Update();
        }

        try
        {
            if (PouchUI.Instance != null)
            {
                var test = InventoryUICheck.CheckLivePouch(mouse, camera);
                try { while(test.MoveNext()) yield return test.Current; }
                finally { (test as IDisposable)?.Dispose(); }
            }
            var lighting = LightingPanelCheck.Run(camera,keyboard,mouse);
            while (lighting.MoveNext()) yield return lighting.Current;
            VerifyNearMissAssist(melee);
            var panel = SkillExplainUI.Instance;
            panel.SetOpen(true);
            float settled = Time.unscaledTime + .5f;
            while (Time.unscaledTime < settled) yield return null;
            Keys(Key.D);
            var start = body.position;
            var scrolling = PresentationRegressionCheck.VerifyPanelScroll(camera,panel,true);
            while (scrolling.MoveNext()) yield return scrolling.Current;
            Require(Vector2.Distance(start,body.position) > .1f, "actual player travels with stats open");
            panel.SetOpen(false);
            var wait = Until(() => player.MoveInput == Vector2.right && body.linearVelocity.x > 0f, "D moves the live player");
            while (wait.MoveNext()) yield return wait.Current;

            player.SetInputBlocked(true);
            Left(true);
            yield return null;
            yield return null;
            Require(player.MoveInput == Vector2.zero && body.linearVelocity == Vector2.zero && swings.Count == 0,
                "room transition blocks both movement and attack");
            // Batch mode can reset the synthetic keyboard while the editor changes focus.
            // Supply the held state while the gameplay gate is still closed.
            Keys(Key.D);
            Require(player.MoveInput == Vector2.zero, "direction events are gated while blocked");
            player.SetInputBlocked(false);
            var move = input.actions.FindAction("Player/Move", true);
            Require(player.MoveInput == Vector2.right, $"held movement restores: input={player.MoveInput}, " +
                $"action={move.ReadValue<Vector2>()}, phase={move.phase}, D={keyboard.dKey.isPressed}, " +
                $"blocked={player.IsInputBlocked}, dead={player.Stat.IsDead}, CC={player.IsCCed}");
            yield return null;
            yield return null;
            Require(swings.Count == 0, "click during room transition stays consumed after unlock");
            Left(false);
            Keys();
            yield return null;
            yield return null;

            Vector2 stepStart = body.position;
            Keys(Key.D);
            Left(true);
            Require(melee.IsAttacking && !melee.IsAimingAttack && swings.Count == 0,
                "directional click reserves the attack before the swing animation and feedback");
            Left(false);
            Keys();
            wait = Until(() => swings.Count == 1, "tapped directional attack swings after its step");
            while (wait.MoveNext()) yield return wait.Current;
            Vector2 step = lastSwingPosition - stepStart;
            const float expectedStep = 0.30f;
            Require(Vector2.Distance(step, Vector2.right * expectedStep) < .015f && lastSwingVelocity.sqrMagnitude < .001f,
                $"directional step is complete at swing start: {step}, velocity={lastSwingVelocity}");
            Require(Vector2.Distance(lastSwingVisualPosition, lastSwingPosition) < .04f && melee.IsAimingAttack,
                "the interpolated body has reached the step endpoint before the swing");
            Vector2 swingAim = melee.CurrentAttackDir;
            float facing = player.transform.localScale.x;
            float normalSpeed = player.Stat.MOVESPEED;
            Keys(Key.W);
            wait = Until(() => body.linearVelocity.y > normalSpeed * .2f, "attack accepts upward movement");
            while (wait.MoveNext()) yield return wait.Current;
            Require(melee.IsAimingAttack && body.position.y > lastSwingPosition.y &&
                    body.linearVelocity.magnitude <= normalSpeed * .251f, "walk slowly during swing");
            Keys(Key.S);
            wait = Until(() => body.linearVelocity.y < -normalSpeed * .2f, "steer back during the same swing");
            while (wait.MoveNext()) yield return wait.Current;
            Require(melee.IsAimingAttack && melee.CurrentAttackDir == swingAim && player.transform.localScale.x == facing &&
                    body.linearVelocity.magnitude <= normalSpeed * .251f, "changing movement keeps swing aim and speed limit");
            Keys();
            wait = Until(() => !melee.IsAttacking, "directional tap finishes");
            while (wait.MoveNext()) yield return wait.Current;
            wait = Until(() => Mathf.Approximately(player.SpeedMultiplier, 1f), "walk speed recovers after swing");
            while (wait.MoveNext()) yield return wait.Current;

            melee.CancelPlayerAttack();
            swings.Clear();
            stepStart = body.position;
            Keys(Key.D);
            Left(true);
            Require(melee.IsAttacking, "second directional attack enters preparation");
            player.SetInputBlocked(true);
            Left(false);
            Keys();
            yield return new WaitForFixedUpdate();
            Require(!melee.IsAttacking && swings.Count == 0 && Vector2.Distance(body.position, stepStart) < .015f,
                "room transition cancels the queued swing and its step");
            player.SetInputBlocked(false);

            int charges = dodge.CurrentCharges;
            Keys(Key.LeftShift);
            wait = Until(() => player.IsDashing, "Shift starts the two-charge dodge");
            while (wait.MoveNext()) yield return wait.Current;
            Require(dodge.CurrentCharges == charges - 1 && player.Stat.Health.Invincible,
                "live dodge spends a charge and owns invulnerability");
            Keys();
            Left(true);
            float comboDeadline = Time.unscaledTime + 4f;
            while (swings.Count < 2 && Time.unscaledTime < comboDeadline)
            {
                yield return null;
                if (!mouse.leftButton.isPressed) Left(true);
            }
            Require(swings.Count >= 2, "attack held through dodge starts and advances the combo");
            Left(false);
            Require(!swingDuringDodge && swings[0] == 0 && swings[1] == 1,
                "dodge keeps its animation and attack resumes as normal combo");

            wait = Until(() => !melee.IsAttacking, "released attack finishes");
            while (wait.MoveNext()) yield return wait.Current;
            melee.CancelPlayerAttack();
            yield return null;
            int afterRelease = swings.Count;
            float releaseDeadline = Time.unscaledTime + .4f;
            while (Time.unscaledTime < releaseDeadline) yield return null;
            Require(swings.Count == afterRelease, "released attack does not repeat");

            var target = GameObject.Find("Enemy Scarecrow");
            Require(target != null, "village scarecrow exists");
            var health = target.GetComponentInChildren<CharacterHealth>();
            Require(health != null, "scarecrow health is wired");
            player.transform.position = target.transform.position + Vector3.left * 1.2f;
            for (int i = 0; i < 12; i++) yield return null;
            var screen = camera.WorldToScreenPoint(target.transform.position);
            pointer = PixelViewport.GameToScreenPoint(screen, PixelViewport.CalculateDisplayRect(Screen.width, Screen.height));
            int hits = 0;
            void OnDamage(int amount, DamageType type, string label, bool critical)
            {
                if (amount > 0) hits++;
            }
            health.TakeDamageEvent += OnDamage;
            try
            {
                Vector2 stationaryStart = body.position;
                Left(true);
                Require(swings.Count == afterRelease + 1 && Vector2.Distance(body.position, stationaryStart) < .001f,
                    "stationary mouse attack starts immediately on the spot");
                Left(false);
                wait = Until(() => hits > 0, "live melee hitbox deals damage to the scarecrow");
                while (wait.MoveNext()) yield return wait.Current;
            }
            finally { health.TakeDamageEvent -= OnDamage; }
            if (ArtDirectionPlayModeCheck.TakeRequest())
            {
                var direction = ArtDirectionPlayModeCheck.Verify(camera, (pressed, point) =>
                {
                    pointer = PixelViewport.GameToScreenPoint(camera.WorldToScreenPoint(point), PixelViewport.CalculateDisplayRect(Screen.width, Screen.height));
                    Left(pressed);
                });
                while (direction.MoveNext()) yield return direction.Current;
            }
            var effects = CameraImpactPlayModeCheck.Run(camera);
            while (effects.MoveNext()) yield return effects.Current;
            Debug.Log($"[PlayerControlPlayModeCheck] PASS: directional step {step.x:F3} before swing, " +
                "charge dodge, held combo, preparation cancel and stationary scarecrow hit.");
        }
        finally
        {
            melee.OnAttackExecuted -= OnSwing;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            InputSystem.settings = previousSettings;
            UnityEngine.Object.Destroy(settings);
        }
    }

    private static void VerifyNearMissAssist(MeleeCombatController melee)
    {
        Vector2 origin=new(10000,10000);
        var target=new GameObject("Aim test hurtbox") { layer=Layers.Enemy };
        var wall=new GameObject("Aim test wall") { layer=Layers.Wall };
        try
        {
            target.transform.position=origin+new Vector2(2.26f,0);
            var box=target.AddComponent<BoxCollider2D>();
            box.size=new Vector2(.4f,.4f);
            target.AddComponent<CharacterHealth>();
            wall.transform.position=origin+Vector2.right;
            wall.AddComponent<BoxCollider2D>();
            wall.SetActive(false);
            Physics2D.SyncTransforms();
            Vector2 pointer=origin+new Vector2(2.26f,.35f);
            Vector2 raw=(pointer-origin).normalized;
            melee.ResolveAim(origin,pointer,Vector2.zero,2,out var aim,out var step);
            Require(aim.y<raw.y && Vector2.Angle(aim,raw)<=8.01f && step.x>.05f && step.magnitude<=.12f,
                "a near miss adjusts aim and adds a small step");
            melee.ResolveAim(origin,pointer,Vector2.left*.3f,2,out _,out step);
            Require(step==Vector2.zero, "intentional retreat receives no extra travel");
            melee.ResolveAim(origin,pointer,Vector2.zero,1.7f,out aim,out step);
            Require(Vector2.Distance(aim,raw)<.0001f && step==Vector2.zero,"out-of-reach target stays a miss");
            wall.SetActive(true); Physics2D.SyncTransforms();
            melee.ResolveAim(origin,pointer,Vector2.zero,2,out aim,out step);
            Require(Vector2.Distance(aim,raw)<.0001f && step==Vector2.zero,"aim does not pull through a wall");
        }
        finally { UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(wall); }
    }

    private static IEnumerator Until(Func<bool> condition, string label)
    {
        float deadline = Time.unscaledTime + 4f;
        while (!condition() && Time.unscaledTime < deadline) yield return null;
        Require(condition(), label);
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[PlayerControlPlayModeCheck] " + label);
    }
}
