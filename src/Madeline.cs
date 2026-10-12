using System;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>Answers "is there a Solid here?" in Celeste pixel space (x right, y down).</summary>
    public interface ISolids
    {
        bool Rect(int left, int top, int width, int height);
        bool Point(int x, int y);
        /// <summary>One-way platforms (Celeste JumpThru): only block falling onto them from above.</summary>
        bool JumpThru(int left, int top, int width, int height);
        /// <summary>
        /// Gentlest slope (degrees from horizontal) of the solid ground right under her feet at (x, y),
        /// and which way is downhill (-1/+1). 0 if there's no sloped solid ground (flat, platform, nothing).
        /// </summary>
        float GroundAngle(int x, int y, out int downhill);
    }

    /// <summary>
    /// Madeline's movement, ported from Celeste's Player (Normal / Dash / Climb states) with the
    /// original constants, timers, buffers and corner corrections. Runs at a fixed 60 Hz in
    /// integer pixels with sub-pixel remainders, exactly like Monocle's Actor.
    /// Addition for Getting Over It's polygon terrain: optional slope step-up/step-down while grounded.
    /// </summary>
    public class Madeline
    {
        // ---- Celeste constants (Player.cs) ------------------------------------------------------------
        public const float MaxFall = 160f, Gravity = 900f, HalfGravThreshold = 40f;
        public const float FastMaxFall = 240f, FastMaxAccel = 300f;
        public const float MaxRun = 90f, RunAccel = 1000f, RunReduce = 400f, AirMult = .65f;
        public const float DuckFriction = 500f, DuckCorrectSlide = 50f;
        public const int DuckCorrectCheck = 4;
        public const float DodgeSlideSpeedMult = 1.2f, DuckSuperJumpXMult = 1.25f, DuckSuperJumpYMult = .5f;
        public const float JumpGraceTime = .1f, JumpSpeed = -105f, JumpHBoost = 40f, VarJumpTime = .2f;
        public const float CeilingVarJumpGrace = .05f;
        public const int UpwardCornerCorrection = 4;
        public const float WallSpeedRetentionTime = .06f;
        public const int WallJumpCheckDist = 3;
        public const float WallJumpForceTime = .16f, WallJumpHSpeed = MaxRun + JumpHBoost;
        public const float WallSlideStartMax = 20f, WallSlideTime = 1.2f;
        public const float SuperJumpH = 260f;
        public const float SuperWallJumpSpeed = -160f, SuperWallJumpVarTime = .25f, SuperWallJumpH = MaxRun + JumpHBoost * 2;
        public const float DashSpeed = 240f, EndDashSpeed = 160f, EndDashUpMult = .75f, DashTime = .15f;
        public const float DashCooldown = .2f, DashRefillCooldown = .1f, DashAttackTime = .3f;
        public const int DashCornerCorrection = 4;
        public const float ClimbMaxStamina = 110f, ClimbUpCost = 100f / 2.2f, ClimbStillCost = 100f / 10f, ClimbJumpCost = 110f / 4f;
        public const int ClimbCheckDist = 2, ClimbUpCheckDist = 2;
        public const float ClimbNoMoveTime = .1f, ClimbTiredThreshold = 20f, ClimbUpSpeed = -45f, ClimbDownSpeed = 80f;
        public const float ClimbSlipSpeed = 30f, ClimbAccel = 900f, ClimbGrabYMult = .2f;
        public const float ClimbHopY = -120f, ClimbHopX = 100f, ClimbHopForceTime = .2f, ClimbJumpBoostTime = .2f;
        public const float DashFreezeTime = .05f;

        public const int StNormal = 0, StClimb = 1, StDash = 2;

        // ---- Options ----------------------------------------------------------------------------------
        public int MaxDashes = 1;
        public bool InfiniteStamina;
        public int SlopeStep = 2;        // px Madeline may step up/down per px of horizontal motion when grounded
        public int LedgeStep = 4;        // small lips/bumps (with flat ground behind) she walks straight over
        public int ClimbContour = 2;     // px per px she follows a wall that leans in/out while climbing
        public float GravityScale = 1f;  // follows GOI's Physics2D.gravity: 1 normally, 0 in space, <0 floats up (ending)
        public Vector2 ExternalAccel;    // px/s^2, Celeste space (y down): GOI's gravity wells in space
        public float GripTime = 3f;      // seconds she can hang on a wall before stamina runs out (0 = Celeste's ~11s)
        public float SlipAngle = 75f;    // ground this steep (or steeper) can't be stood on: she slides off (0 = off)
        public float SlipSpeed = 70f;    // px/s she's pushed downhill while slipping
        public bool Slipping;
        public float GroundSlope;        // debug: slope of the rock she's standing on (degrees)
        int slipDir;

        /// <summary>Sound cue: (name, direction). Mirrors where Celeste's Player calls Audio.Play.</summary>
        public Action<string, int> Sfx;
        float playFootstepOnLand;
        bool wasDucking;
        bool dashPink;

        // ---- State ------------------------------------------------------------------------------------
        public int X, Y;                 // feet, bottom-centre of the hitbox (Celeste "Position")
        Vector2 remainder;
        public Vector2 Speed;
        public int Facing = 1;
        public int State = StNormal;
        public int Dashes = 1;
        public float Stamina = ClimbMaxStamina;
        public bool OnGround;
        public bool Ducking;
        public Vector2 DashDir;
        public int MoveX;
        public int WallSlideDir;
        public int LastClimbMove;
        public bool FastJump;
        public Vector2 SpriteScale = Vector2.one;

        public bool AutoJump;
        float autoJumpTimer;
        float jumpGraceTimer, varJumpTimer, varJumpSpeed;
        float dashCooldownTimer, dashRefillCooldownTimer, dashAttackTimer;
        float maxFall = MaxFall;
        float forceMoveXTimer; int forceMoveX;
        float wallSlideTimer = WallSlideTime;
        float wallSpeedRetentionTimer, wallSpeedRetained;
        float wallBoostTimer; int wallBoostDir;
        float climbNoMoveTimer;
        int hopWaitX; float hopWaitXSpeed;
        bool dashStartedOnGround;
        Vector2 beforeDashSpeed, lastAim;
        int dashCo;                      // 0 none, 1 waiting to pick direction, 2 timing
        float dashCoTimer;
        public float FreezeTimer;
        public int DashTrailRequests;    // view creates a trail snapshot per request

        readonly ISolids solids;
        CelesteInput input;

        public Madeline(ISolids solids) { this.solids = solids; }

        // ---- Hitbox (normal 8x11, ducking 8x6, both anchored at the feet) ------------------------------
        public int HitboxHeight { get { return Ducking ? 6 : 11; } }
        bool CollideAt(int x, int y) { return solids.Rect(x - 4, y - HitboxHeight, 8, HitboxHeight); }
        bool CollideAt(int x, int y, int h) { return solids.Rect(x - 4, y - h, 8, h); }
        bool CanUnDuckAt(int x, int y) { return !CollideAt(x, y, 11); }
        bool CanUnDuck { get { return !Ducking || CanUnDuckAt(X, Y); } }
        bool DuckFreeAt(int x, int y) { return !CollideAt(x, y, 6); }
        bool JumpThruAt(int x, int y) { return solids.JumpThru(x - 4, y - HitboxHeight, 8, HitboxHeight); }
        // Celeste: CollideCheck<Solid> || CollideCheckOutside<JumpThru>
        bool OnGroundAt(int x, int y) { return CollideAt(x, y + 1) || (JumpThruAt(x, y + 1) && !JumpThruAt(x, y)); }
        void Play(string name, int dir) { if (Sfx != null) Sfx(name, dir); }
        public bool IsTired { get { return CheckStamina < ClimbTiredThreshold; } }
        float CheckStamina { get { return wallBoostTimer > 0 ? Stamina + ClimbJumpCost : Stamina; } }
        bool DashAttacking { get { return dashAttackTimer > 0; } }
        bool CanDash { get { return input.DashPressed && dashCooldownTimer <= 0 && Dashes > 0; } }
        bool SuperWallJumpAngleCheck { get { return Math.Abs(DashDir.x) <= 0.2f && DashDir.y <= -0.75f; } }
        bool ClimbCheck(int dir, int yAdd) { return CollideAt(X + dir * ClimbCheckDist, Y + yAdd); }
        bool WallJumpCheck(int dir) { return CollideAt(X + dir * WallJumpCheckDist, Y); }
        public bool Colliding { get { return CollideAt(X, Y); } }
        public bool SolidPoint(int x, int y) { return solids.Point(x, y); }
        public bool CollideCheck(int x, int y) { return CollideAt(x, y); }

        public void Teleport(int x, int y)
        {
            X = x; Y = y; remainder = Vector2.zero; Speed = Vector2.zero;
            State = StNormal; dashCo = 0; Ducking = false;
        }

        // ================================================================================================
        public void Update(float dt, CelesteInput inp)
        {
            input = inp;
            if (FreezeTimer > 0) { FreezeTimer -= dt; return; }
            if (playFootstepOnLand > 0) playFootstepOnLand -= dt;

            OnGround = Speed.y >= 0 && OnGroundAt(X, Y);

            // GOI tweak: too-steep rock is not ground. She slides off it with no refills and no jumping,
            // so a bad landing on a steep face costs you, like it would Diogenes.
            Slipping = false;
            GroundSlope = 0;
            if (OnGround && SlipAngle > 0 && State != StClimb)
            {
                int dir;
                float angle = solids.GroundAngle(X, Y, out dir);
                GroundSlope = angle;
                if (angle >= SlipAngle && dir != 0)
                {
                    OnGround = false;
                    Slipping = true;
                    slipDir = dir;
                }
            }

            // Dashes
            if (dashCooldownTimer > 0) dashCooldownTimer -= dt;
            if (dashRefillCooldownTimer > 0) dashRefillCooldownTimer -= dt;
            else if (OnGround) RefillDash();

            if (varJumpTimer > 0) varJumpTimer -= dt;
            if (autoJumpTimer > 0)
            {
                if (AutoJump) { autoJumpTimer -= dt; if (autoJumpTimer <= 0) AutoJump = false; }
                else autoJumpTimer = 0;
            }

            if (forceMoveXTimer > 0) { forceMoveXTimer -= dt; MoveX = forceMoveX; }
            else MoveX = input.MoveX;

            if (MoveX != 0 && State != StClimb)
            {
                if (MoveX != Facing && Ducking) SpriteScale = new Vector2(0.8f, 1.2f);
                Facing = MoveX;
            }
            lastAim = input.GetAim(Facing);

            // Wall speed retention
            if (wallSpeedRetentionTimer > 0)
            {
                if (Math.Sign(Speed.x) == -Math.Sign(wallSpeedRetained)) wallSpeedRetentionTimer = 0;
                else if (!CollideAt(X + Math.Sign(wallSpeedRetained), Y)) { Speed.x = wallSpeedRetained; wallSpeedRetentionTimer = 0; }
                else wallSpeedRetentionTimer -= dt;
            }

            // Hop wait (after climbing over a ledge)
            if (hopWaitX != 0)
            {
                if (Math.Sign(Speed.x) == -hopWaitX || Speed.y > 0) hopWaitX = 0;
                else if (!CollideAt(X + hopWaitX, Y)) { Speed.x = hopWaitXSpeed; hopWaitX = 0; }
            }

            if (WallSlideDir != 0) { wallSlideTimer = Math.Max(wallSlideTimer - dt, 0); WallSlideDir = 0; }

            if (wallBoostTimer > 0)
            {
                wallBoostTimer -= dt;
                if (MoveX == wallBoostDir) { Speed.x = WallJumpHSpeed * MoveX; Stamina += ClimbJumpCost; wallBoostTimer = 0; }
            }

            if (OnGround && State != StClimb) { AutoJump = false; Stamina = ClimbMaxStamina; wallSlideTimer = WallSlideTime; }
            if (InfiniteStamina) Stamina = ClimbMaxStamina;

            if (dashAttackTimer > 0) dashAttackTimer -= dt;
            if (OnGround) jumpGraceTimer = JumpGraceTime;
            else if (jumpGraceTimer > 0) jumpGraceTimer -= dt;

            // ---- State machine ----
            int prevState = State;
            int next = State == StNormal ? NormalUpdate(dt) : State == StClimb ? ClimbUpdate(dt) : DashUpdate();
            SetState(next);
            // Monocle runs a freshly started coroutine up to its first "yield return null" (a no-op here),
            // so the dash direction is only chosen on the following frame.
            if (State == StDash && prevState == StDash) DashCoroutine(dt);

            // ---- Physics ----
            bool groundMode = OnGround && State != StClimb && Speed.y >= 0;
            MoveH(Speed.x * dt, groundMode);
            MoveV(Speed.y * dt);

            if (Ducking != wasDucking && OnGround && State == StNormal) Play(Ducking ? "duck" : "stand", 0);
            wasDucking = Ducking;

            SpriteScale.x = Approach(SpriteScale.x, 1f, 1.75f * dt);
            SpriteScale.y = Approach(SpriteScale.y, 1f, 1.75f * dt);
        }

        void SetState(int next)
        {
            if (next == State) return;
            int prev = State;
            State = next;
            if (prev == StDash) dashCo = 0;
            if (next == StNormal) maxFall = MaxFall;
            else if (next == StClimb) ClimbBegin();
            else if (next == StDash) DashBegin();
        }

        void RefillDash() { if (Dashes < MaxDashes) Dashes = MaxDashes; }

        /// <summary>Celeste's Player.UseRefill (green crystal): true if it actually gave something back.</summary>
        public bool UseRefill()
        {
            if (Dashes < MaxDashes || Stamina < ClimbTiredThreshold)
            {
                Dashes = MaxDashes;
                Stamina = ClimbMaxStamina;
                FreezeTimer = DashFreezeTime;
                return true;
            }
            return false;
        }

        /// <summary>Horizontal gap (px) from her side to the rock at a given row, or -1 if none within reach.</summary>
        public int WallGap(int row, int reach)
        {
            int ax = Facing == 1 ? X + 4 : X - 5;
            for (int k = 0; k <= reach; k++) if (solids.Point(ax + Facing * k, row)) return k;
            return -1;
        }

        // ================================================================================================
        int NormalUpdate(float dt)
        {
            // Climbing
            // GOI tweak: standing on the ground she only grabs walls that reach her hands,
            // so knee-high pebbles and bumps in the rock are walked over instead of "climbed".
            if (input.GrabCheck && !IsTired && !Ducking && (!OnGround || HandsOnWall(ClimbCheckDist)))
            {
                if (Speed.y >= 0 && Math.Sign(Speed.x) != -Facing)
                {
                    if (ClimbCheck(Facing, 0)) { Ducking = false; return StClimb; }
                    if (input.MoveY < 1)
                    {
                        for (int i = 1; i <= ClimbUpCheckDist; i++)
                        {
                            if (!CollideAt(X, Y - i) && ClimbCheck(Facing, -i))
                            {
                                MoveVExact(-i);
                                Ducking = false;
                                return StClimb;
                            }
                        }
                    }
                }
            }

            // Dashing
            if (CanDash) return StartDash();

            // Ducking
            if (Ducking)
            {
                if (OnGround && input.MoveY != 1)
                {
                    if (CanUnDuck) Ducking = false;
                    else if (Speed.x == 0)
                    {
                        for (int i = DuckCorrectCheck; i > 0; i--)
                        {
                            if (CanUnDuckAt(X + i, Y)) { MoveH(DuckCorrectSlide * dt, false); break; }
                            if (CanUnDuckAt(X - i, Y)) { MoveH(-DuckCorrectSlide * dt, false); break; }
                        }
                    }
                }
            }
            else if (OnGround && input.MoveY == 1 && Speed.y >= 0)
            {
                Ducking = true;
                SpriteScale = new Vector2(1.4f, 0.6f);
            }

            // Running and friction
            if (Slipping)
            {
                // pushed off the steep face; holding downhill only speeds it up
                float target = slipDir * (MoveX == slipDir ? MaxRun : SlipSpeed);
                Speed.x = Approach(Speed.x, target, RunAccel * dt);
            }
            else if (Ducking && OnGround) Speed.x = Approach(Speed.x, 0, DuckFriction * dt);
            else
            {
                float mult = OnGround ? 1 : AirMult;
                if (Math.Abs(Speed.x) > MaxRun && Math.Sign(Speed.x) == MoveX)
                    Speed.x = Approach(Speed.x, MaxRun * MoveX, RunReduce * mult * dt);
                else
                    Speed.x = Approach(Speed.x, MaxRun * MoveX, RunAccel * mult * dt);
            }

            // Fast fall
            if (input.MoveY == 1 && Speed.y >= MaxFall) maxFall = Approach(maxFall, FastMaxFall, FastMaxAccel * dt);
            else maxFall = Approach(maxFall, MaxFall, FastMaxAccel * dt);

            // Gravity
            if (!OnGround)
            {
                float max = maxFall;
                if ((MoveX == Facing || (MoveX == 0 && input.GrabCheck)) && input.MoveY != 1)
                {
                    if (Speed.y >= 0 && wallSlideTimer > 0 && CollideAt(X + Facing, Y) && CanUnDuck)
                    {
                        Ducking = false;
                        WallSlideDir = Facing;
                    }
                    if (WallSlideDir != 0) max = Mathf.Lerp(MaxFall, WallSlideStartMax, wallSlideTimer / WallSlideTime);
                }
                float gmult = (Math.Abs(Speed.y) < HalfGravThreshold && (input.JumpCheck || AutoJump)) ? .5f : 1f;
                if (GravityScale > 0) Speed.y = Approach(Speed.y, max, Gravity * gmult * GravityScale * dt);
                else if (GravityScale < 0) Speed.y = Approach(Speed.y, -max, Gravity * -GravityScale * dt);
                // GravityScale == 0: zero-g (GOI's space). Keep whatever momentum she launched with.
            }
            if (ExternalAccel != Vector2.zero && !OnGround) Speed += ExternalAccel * dt;

            // Variable jumping
            if (varJumpTimer > 0)
            {
                if (AutoJump || input.JumpCheck) Speed.y = Math.Min(Speed.y, varJumpSpeed);
                else varJumpTimer = 0;
            }

            // Jumping
            if (input.JumpPressed)
            {
                if (jumpGraceTimer > 0) Jump();
                else if (CanUnDuck)
                {
                    if (WallJumpCheck(1))
                    {
                        if (Facing == 1 && input.GrabCheck && Stamina > 0) ClimbJump();
                        else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(-1);
                        else WallJump(-1);
                    }
                    else if (WallJumpCheck(-1))
                    {
                        if (Facing == -1 && input.GrabCheck && Stamina > 0) ClimbJump();
                        else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(1);
                        else WallJump(1);
                    }
                }
            }
            return StNormal;
        }

        void Jump() { Jump(true); }

        void Jump(bool sfx)
        {
            if (sfx) Play("jump", 0);
            input.ConsumeJump();
            jumpGraceTimer = 0; varJumpTimer = VarJumpTime; AutoJump = false; dashAttackTimer = 0;
            wallSlideTimer = WallSlideTime; wallBoostTimer = 0;
            Speed.x += JumpHBoost * MoveX;
            Speed.y = JumpSpeed;
            varJumpSpeed = Speed.y;
            SpriteScale = new Vector2(.6f, 1.4f);
        }

        void SuperJump()
        {
            Play(Ducking ? "superslide" : "super", Facing);
            input.ConsumeJump();
            jumpGraceTimer = 0; varJumpTimer = VarJumpTime; AutoJump = false; dashAttackTimer = 0;
            wallSlideTimer = WallSlideTime; wallBoostTimer = 0;
            Speed.x = SuperJumpH * Facing;
            Speed.y = JumpSpeed;
            if (Ducking)
            {
                Ducking = false;
                Speed.x *= DuckSuperJumpXMult;
                Speed.y *= DuckSuperJumpYMult;
                SpriteScale = new Vector2(1.5f, .5f);
            }
            else SpriteScale = new Vector2(.6f, 1.4f);
            varJumpSpeed = Speed.y;
        }

        void WallJump(int dir)
        {
            Play("walljump", dir);
            Ducking = false;
            input.ConsumeJump();
            jumpGraceTimer = 0; varJumpTimer = VarJumpTime; AutoJump = false; dashAttackTimer = 0;
            wallSlideTimer = WallSlideTime; wallBoostTimer = 0;
            if (MoveX != 0) { forceMoveX = dir; forceMoveXTimer = WallJumpForceTime; }
            Speed.x = WallJumpHSpeed * dir;
            Speed.y = JumpSpeed;
            varJumpSpeed = Speed.y;
            SpriteScale = new Vector2(.6f, 1.4f);
        }

        void SuperWallJump(int dir)
        {
            Play("superwall", dir);
            Ducking = false;
            input.ConsumeJump();
            jumpGraceTimer = 0; varJumpTimer = SuperWallJumpVarTime; AutoJump = false; dashAttackTimer = 0;
            wallSlideTimer = WallSlideTime; wallBoostTimer = 0;
            Speed.x = SuperWallJumpH * dir;
            Speed.y = SuperWallJumpSpeed;
            varJumpSpeed = Speed.y;
            SpriteScale = new Vector2(.6f, 1.4f);
        }

        void ClimbJump()
        {
            if (!OnGround) Stamina -= ClimbJumpCost;
            Play("climbjump", Facing);
            Jump(false);
            if (MoveX == 0) { wallBoostDir = -Facing; wallBoostTimer = ClimbJumpBoostTime; }
        }

        // ================================================================================================
        int StartDash()
        {
            dashPink = Dashes >= 2;
            Dashes = Math.Max(0, Dashes - 1);
            input.ConsumeDash();
            return StDash;
        }

        void DashBegin()
        {
            dashStartedOnGround = OnGround;
            FreezeTimer = DashFreezeTime;
            dashCooldownTimer = DashCooldown;
            dashRefillCooldownTimer = DashRefillCooldown;
            wallSlideTimer = WallSlideTime;
            dashAttackTimer = DashAttackTime;
            beforeDashSpeed = Speed;
            Speed = Vector2.zero;
            DashDir = Vector2.zero;
            if (!OnGround && Ducking && CanUnDuck) Ducking = false;
            dashCo = 1;
        }

        int DashUpdate()
        {
            if (DashDir.y == 0)
            {
                // Super / hyper jump
                if (CanUnDuck && input.JumpPressed && jumpGraceTimer > 0) { SuperJump(); return StNormal; }
            }
            if (DashDir.x == 0 && DashDir.y == -1)
            {
                if (input.JumpPressed && CanUnDuck)
                {
                    if (WallJumpCheck(1)) { SuperWallJump(-1); return StNormal; }
                    if (WallJumpCheck(-1)) { SuperWallJump(1); return StNormal; }
                }
            }
            else if (input.JumpPressed && CanUnDuck)
            {
                if (WallJumpCheck(1)) { WallJump(-1); return StNormal; }
                if (WallJumpCheck(-1)) { WallJump(1); return StNormal; }
            }
            return StDash;
        }

        void DashCoroutine(float dt)
        {
            if (dashCo == 1)
            {
                var dir = lastAim;
                var newSpeed = dir * DashSpeed;
                if (Math.Sign(beforeDashSpeed.x) == Math.Sign(newSpeed.x) && Math.Abs(beforeDashSpeed.x) > Math.Abs(newSpeed.x))
                    newSpeed.x = beforeDashSpeed.x;
                Speed = newSpeed;
                DashDir = dir;
                if (DashDir.x != 0) Facing = Math.Sign(DashDir.x);
                Play(dashPink ? "dashpink" : "dash", Facing);

                // Dash slide (diagonal down-dash started on the ground)
                if (OnGround && DashDir.x != 0 && DashDir.y > 0 && Speed.y > 0)
                {
                    DashDir.x = Math.Sign(DashDir.x);
                    DashDir.y = 0;
                    Speed.y = 0;
                    Speed.x *= DodgeSlideSpeedMult;
                    Ducking = true;
                }
                DashTrailRequests++;
                dashCo = 2;
                dashCoTimer = DashTime;
            }
            else if (dashCo == 2)
            {
                if (dashCoTimer > 0) { dashCoTimer -= dt; return; }
                DashTrailRequests++;
                AutoJump = true;
                autoJumpTimer = 0;
                if (DashDir.y <= 0) Speed = DashDir * EndDashSpeed;
                if (Speed.y < 0) Speed.y *= EndDashUpMult;
                dashCo = 0;
                SetState(StNormal);
            }
        }

        // ================================================================================================
        void ClimbBegin()
        {
            Play("grab", 0);
            AutoJump = false;
            Speed.x = 0;
            Speed.y *= ClimbGrabYMult;
            wallSlideTimer = WallSlideTime;
            climbNoMoveTimer = ClimbNoMoveTime;
            wallBoostTimer = 0;
            LastClimbMove = 0;
            for (int i = 0; i < ClimbCheckDist; i++)
            {
                if (!CollideAt(X + Facing, Y)) X += Facing;
                else break;
            }
        }

        // ---- GOI contour following -------------------------------------------------------------------
        // Celeste walls are perfectly vertical. GOI's rocks lean, bulge and have seams, so while climbing
        // Madeline hugs the face: she shuffles toward a wall that leans away, and steps out around one that
        // leans in, instead of losing her grip (which Celeste turns into a climb-hop).

        /// <summary>Wall touching at (X + Facing, y), or within ClimbContour px - then move flush to it.</summary>
        bool HugWall(int y)
        {
            if (CollideAt(X + Facing, y)) return true;
            for (int k = 1; k <= ClimbContour; k++)
            {
                if (CollideAt(X + Facing * k, y)) return false;      // path blocked (shouldn't happen)
                if (CollideAt(X + Facing * (k + 1), y)) { X += Facing * k; return true; }
            }
            return false;
        }

        /// <summary>Climbing up into a face that leans over her: step out from the wall to keep going.</summary>
        bool StepOutFromWall()
        {
            for (int k = 1; k <= ClimbContour; k++)
            {
                if (CollideAt(X - Facing * k, Y)) return false;
                if (!CollideAt(X - Facing * k, Y - 1)) { X -= Facing * k; return true; }
            }
            return false;
        }

        bool WallWithin(int y)
        {
            for (int k = 1; k <= ClimbContour + 1; k++) if (CollideAt(X + Facing * k, y)) return true;
            return false;
        }

        /// <summary>Is there rock in front of her upper body (hands), within reach?</summary>
        bool HandsOnWall(int reach)
        {
            int top = Y - HitboxHeight;
            int ax = Facing == 1 ? X + 4 : X - 5;
            for (int k = 0; k <= reach + ClimbContour; k++)
            {
                int px = ax + Facing * k;
                if (solids.Point(px, top + 1) || solids.Point(px, top + 4)) return true;
            }
            return false;
        }

        int ClimbUpdate(float dt)
        {
            climbNoMoveTimer -= dt;
            if (OnGround) Stamina = ClimbMaxStamina;

            // Wall jump / climb jump
            if (input.JumpPressed && (!Ducking || CanUnDuck))
            {
                if (MoveX == -Facing) WallJump(-Facing);
                else ClimbJump();
                return StNormal;
            }

            if (CanDash) return StartDash();

            if (!input.GrabCheck) { Play("letgo", 0); return StNormal; }

            // No wall to hold (even after hugging the contour)
            if (!HugWall(Y))
            {
                if (Speed.y < 0) ClimbHop();
                return StNormal;
            }

            float target = 0;
            bool trySlip = false;
            if (climbNoMoveTimer <= 0)
            {
                if (input.MoveY == -1)
                {
                    target = ClimbUpSpeed;
                    bool blocked = CollideAt(X, Y - 1) && !StepOutFromWall();
                    if (blocked || (ClimbHopBlockedCheck() && SlipCheck(-1)))
                    {
                        if (Speed.y < 0) Speed.y = 0;
                        target = 0;
                        trySlip = true;
                    }
                    else if (SlipCheck(0))
                    {
                        ClimbHop();
                        return StNormal;
                    }
                }
                else if (input.MoveY == 1)
                {
                    target = ClimbDownSpeed;
                    if (OnGround) { if (Speed.y > 0) Speed.y = 0; target = 0; }
                }
                else trySlip = true;
            }
            else trySlip = true;

            LastClimbMove = Math.Sign(target);
            if (trySlip && SlipCheck(0)) target = ClimbSlipSpeed;
            Speed.y = Approach(Speed.y, target, ClimbAccel * dt);

            // Down limit: don't slide off the bottom of the wall
            if (input.MoveY != 1 && Speed.y > 0 && !WallWithin(Y + 1)) Speed.y = 0;

            if (climbNoMoveTimer <= 0 && !InfiniteStamina)
            {
                // GripTime caps how long any hold lasts; once she's out she can't grab and slides down
                // the wall (Celeste's tired wall-slide).
                float still = GripTime > 0 ? ClimbMaxStamina / GripTime : ClimbStillCost;
                float up = Math.Max(ClimbUpCost, still);
                if (LastClimbMove == -1) Stamina -= up * dt;
                else if (LastClimbMove == 0) Stamina -= still * dt;
            }

            if (Stamina <= 0) return StNormal;
            return StClimb;
        }

        void ClimbHop()
        {
            Play("ledge", 0);
            playFootstepOnLand = 0.5f;
            if (CollideAt(X + Facing, Y)) { hopWaitX = Facing; hopWaitXSpeed = Facing * ClimbHopX; }
            else { hopWaitX = 0; Speed.x = Facing * ClimbHopX; }
            Speed.y = Math.Min(Speed.y, ClimbHopY);
            forceMoveX = 0;
            forceMoveXTimer = ClimbHopForceTime;
        }

        /// <summary>
        /// Hands above the top of the wall? Celeste's two-point check, widened so a face that leans back
        /// (up to the steepest walkable slope, ~27° off vertical => ~4px over her height) still counts as wall.
        /// </summary>
        bool SlipCheck(int addY)
        {
            int top = Y - HitboxHeight;
            int ax = Facing == 1 ? X + 4 : X - 4 - 1;
            int ay = top + 4 + addY;
            int reach = ClimbContour > 0 ? ClimbContour + 3 : 0;
            for (int k = 0; k <= reach; k++)
            {
                int px = ax + Facing * k;
                if (solids.Point(px, ay) || solids.Point(px, ay - 4 + addY)) return false;
            }
            return true;
        }

        bool ClimbHopBlockedCheck() { return CollideAt(X, Y - 6); }

        // ================================================================================================
        // Actor movement (Monocle): accumulate sub-pixels, move one pixel at a time.
        void MoveH(float amount, bool groundMode)
        {
            remainder.x += amount;
            int move = (int)Math.Round(remainder.x);
            if (move != 0)
            {
                remainder.x -= move;
                MoveHExact(move, groundMode);
            }
        }

        void MoveV(float amount)
        {
            remainder.y += amount;
            int move = (int)Math.Round(remainder.y);
            if (move != 0)
            {
                remainder.y -= move;
                MoveVExact(move);
            }
        }

        bool MoveHExact(int move, bool groundMode)
        {
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (CollideAt(X + sign, Y))
                {
                    // Step-up (GOI rocks are polygons, Celeste has none): slopes up to SlopeStep px per px,
                    // and small lips up to LedgeStep px when there's level footing just past them.
                    bool stepped = false;
                    if (groundMode)
                    {
                        int maxStep = Math.Max(SlopeStep, LedgeStep);
                        for (int s = 1; s <= maxStep; s++)
                        {
                            if (CollideAt(X, Y - s)) break;              // head would hit something
                            if (CollideAt(X + sign, Y - s)) continue;    // still blocked; try higher
                            if (s <= SlopeStep || (!CollideAt(X + 2 * sign, Y - s) && !CollideAt(X + 3 * sign, Y - s)))
                            {
                                Y -= s;
                                stepped = true;
                            }
                            break;
                        }
                    }
                    if (!stepped)
                    {
                        remainder.x = 0;
                        OnCollideH();
                        return true;
                    }
                }
                X += sign;
                move -= sign;
                // Step-down: stay glued to descending/bumpy ground instead of micro-falling
                if (groundMode && !OnGroundAt(X, Y))
                {
                    for (int d = 1; d <= Math.Max(SlopeStep, LedgeStep); d++)
                    {
                        if (OnGroundAt(X, Y + d)) { Y += d; break; }
                    }
                }
            }
            return false;
        }

        bool MoveVExact(int move)
        {
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (CollideAt(X, Y + sign) || (sign > 0 && JumpThruAt(X, Y + 1) && !JumpThruAt(X, Y)))
                {
                    remainder.y = 0;
                    OnCollideV();
                    return true;
                }
                Y += sign;
                move -= sign;
            }
            return false;
        }

        void OnCollideH()
        {
            if (State == StDash)
            {
                if (OnGround && DuckFreeAt(X + Math.Sign(Speed.x), Y)) { Ducking = true; return; }
                if (Speed.y == 0 && Speed.x != 0)
                {
                    for (int i = 1; i <= DashCornerCorrection; i++)
                        for (int j = 1; j >= -1; j -= 2)
                            if (!CollideAt(X + Math.Sign(Speed.x), Y + i * j))
                            {
                                MoveVExact(i * j);
                                MoveHExact(Math.Sign(Speed.x), false);
                                return;
                            }
                }
            }
            if (wallSpeedRetentionTimer <= 0)
            {
                wallSpeedRetained = Speed.x;
                wallSpeedRetentionTimer = WallSpeedRetentionTime;
            }
            Speed.x = 0;
            dashAttackTimer = 0;
        }

        void OnCollideV()
        {
            if (Speed.y < 0 && Math.Abs(GravityScale) < 0.01f && State != StClimb)
            {
                // Zero-g (GOI's space): glance off rock above instead of stopping dead. Slide toward the
                // nearer way around it and keep the upward momentum, so a launch from the antenna carries
                // on around the asteroids.
                for (int i = 1; i <= 24; i++)
                {
                    int dir = 0;
                    bool left = !CollideAt(X - i, Y - 1) && !CollideAt(X - i, Y);
                    bool right = !CollideAt(X + i, Y - 1) && !CollideAt(X + i, Y);
                    if (left && right) dir = Speed.x < 0 ? -1 : 1;
                    else if (left) dir = -1;
                    else if (right) dir = 1;
                    if (dir == 0) continue;
                    float push = Math.Max(40f, -Speed.y * 0.8f);
                    if (Math.Sign(Speed.x) != dir || Math.Abs(Speed.x) < push) Speed.x = dir * push;
                    remainder.y = 0;
                    return;                                   // keep Speed.y: she skims around it
                }
            }
            if (Speed.y < 0)
            {
                // Upward corner correction
                if (Speed.x <= 0)
                    for (int i = 1; i <= UpwardCornerCorrection; i++)
                        if (!CollideAt(X - i, Y - 1)) { X -= i; Y -= 1; return; }
                if (Speed.x >= 0)
                    for (int i = 1; i <= UpwardCornerCorrection; i++)
                        if (!CollideAt(X + i, Y - 1)) { X += i; Y -= 1; return; }
                if (varJumpTimer < VarJumpTime - CeilingVarJumpGrace) varJumpTimer = 0;
            }
            else if (Speed.y > 0)
            {
                if (State == StDash && !dashStartedOnGround)
                {
                    if (Speed.x <= 0)
                        for (int i = -1; i >= -DashCornerCorrection; i--)
                            if (!OnGroundAt(X + i, Y)) { MoveHExact(i, false); MoveVExact(1); return; }
                    if (Speed.x >= 0)
                        for (int i = 1; i <= DashCornerCorrection; i++)
                            if (!OnGroundAt(X + i, Y)) { MoveHExact(i, false); MoveVExact(1); return; }
                }
                // Dash slide: landing a diagonal down-dash (the wavedash setup)
                if (State == StDash && DashDir.x != 0 && DashDir.y > 0)
                {
                    DashDir.x = Math.Sign(DashDir.x);
                    DashDir.y = 0;
                    Speed.x *= DodgeSlideSpeedMult;
                    Ducking = true;
                }
                if (State != StClimb)
                {
                    // (skip the tiny re-landings while sliding down a steep face)
                    if (!Slipping || Speed.y > 60) Play(playFootstepOnLand > 0 ? "footstep" : "land", 0);
                    playFootstepOnLand = 0;
                    float squish = Math.Min(Speed.y / FastMaxFall, 1);
                    SpriteScale = new Vector2(Mathf.Lerp(1, 1.6f, squish), Mathf.Lerp(1, .4f, squish));
                }
            }
            Speed.y = 0;
        }

        static float Approach(float val, float target, float maxMove)
        {
            return val > target ? Math.Max(val - maxMove, target) : Math.Min(val + maxMove, target);
        }
    }
}
