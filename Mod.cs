using MinHook;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Y5Lib;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    public unsafe class Mod : Y5Mod
    {

        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

        public static string ModPath;

        public static HookEngine engine = new HookEngine();

        public static Fighter CoopPlayer = null;
        public static EntityHandle CoopPlayerHandle;

        public static int m_coopPlayerIdx = -1;
        private bool m_awaitingSpawn = false;

        private bool m_playerSpawnedDoOnce = false;

        public float CoopPlayerExistTime = 0;
        public float CoopPlayerDoesntExistTime = 0;
        public float CurrentMissionTime = 0;
        private static uint m_currentMission;

        public static bool AutomaticallyCreatePlayer = true;
        public static int[] BlacklistedMissions = new int[0];

        public static float TeleportDistance = 20;

        public static bool DebugInput = true;

        public static string CoopPlayerCommandset = "";

        //Fighter command and property.bin belongs to our mod
        public static bool IsIchibanMovesetEnabled;

        private static float m_teleportDelay = 0;
        private static bool m_remakePlayer;
        private static bool m_dontRespawnPlayerThisMissionDoOnce = false;
        private static bool m_battleStartRecreatePlayer = false;
        private static bool m_btlstDecided = false;
        private delegate ulong DancerDestructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5);
        private delegate ulong DancerDestructor2(IntPtr dancer);
        private delegate ulong FighterPreDestroy(IntPtr dancer);
        private unsafe delegate void FighterModeEquipUpdate(IntPtr fighterMode);

        FighterPreDestroy m_fighterPreDestroyOrig;
        ulong Fighter_PreDestroy(IntPtr fighterPtr)
        {
            //Resetting input like this is necessary to prevent crashes.
            if(fighterPtr == ActionFighterManager.GetFighter(0).Pointer || (ActionFighterManager.IsFighterPresent(m_coopPlayerIdx) && CoopPlayer.Pointer == fighterPtr))
            {
                Fighter fighter = new Fighter() { Pointer = fighterPtr };
                fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
            }

            return m_fighterPreDestroyOrig(fighterPtr);
        }

        private static FighterModeEquipUpdate m_fighterModeEquipUpdateOrig;
        private static void FighterMode_Equip_Update(IntPtr fighterModePtr)
        {
            FighterMode mode = new FighterMode() { Pointer = fighterModePtr };
            Fighter fighter = mode.Fighter;

            if (CoopPlayerHandle.IsValid() && fighter.Index == m_coopPlayerIdx)
                ActionFighterManager.SetPlayer(m_coopPlayerIdx);

            m_fighterModeEquipUpdateOrig.Invoke(fighterModePtr);
        }


        DancerDestructor dieDancer;
        ulong Dancer_Destructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5)
        {
            m_dontRespawnPlayerThisMissionDoOnce = true;

            //We must send a destroy signal to ActionFighterManager precisely at this function
            //Otherwise, our game will get stuck!
            //How the fuck does this happen? How did i even fix this?
            //..i have no idea!
            if (m_coopPlayerIdx > -1)
            {
                PlayerInput.ResetPlayer2Input();
                ActionFighterManager.DestroyFighter(m_coopPlayerIdx);
                Reset();
            }
            return dieDancer(dancer, idk, idk2, idk3, idk4, idk5);
        }

        DancerDestructor2 dieLiveDancer;
        ulong LiveDancer_Destructor(IntPtr dancer)
        {
            m_dontRespawnPlayerThisMissionDoOnce = true;

            //We must send a destroy signal to ActionFighterManager precisely at this function
            //Otherwise, our game will get stuck!
            //How the fuck does this happen? How did i even fix this?
            //..i have no idea!
            if (m_coopPlayerIdx > -1)
            {
                DestroyPlayer2();
            }
            return dieLiveDancer(dancer);
        }

        public override void OnModInit()
        {
            base.OnModInit();
            OE.LogInfo("Y5 Coop Init Start");

            Assembly assmb = Assembly.GetExecutingAssembly();
            ModPath = Path.GetDirectoryName(assmb.Location);

            OE.RegisterJob(Update, 10);

            Thread thread = new Thread(InputThread);
            thread.Start();

            //Warning: Really bad pattern
            dieDancer = engine.CreateHook<DancerDestructor>(CPP.PatternSearch("41 56 48 83 EC ? 48 C7 44 24 20 ? ? ? ? 48 89 5C 24 40 48 89 6C 24 48 48 89 74 24 50 48 89 7C 24 58 44 8B F2 48 8B F1 48 8D 05 ? ? ? ? 48 89 01 48 8D 99 30 17 00 00"), Dancer_Destructor);
            dieLiveDancer = engine.CreateHook<DancerDestructor2>(CPP.PatternSearch("40 53 48 83 EC ? 48 8B D9 48 8B 89 ? ? ? ? 48 8B 01 FF 50 ? F6 83"), LiveDancer_Destructor);
            m_fighterPreDestroyOrig = engine.CreateHook<FighterPreDestroy>(CPP.PatternSearch("40 53 48 83 EC ? 48 8B 01 BA ? ? ? ? 48 8B D9 FF 90 ? ? ? ? 48 8B CB E8 ? ? ? ? 48 8B CB"), Fighter_PreDestroy);
            m_fighterModeEquipUpdateOrig = engine.CreateHook<FighterModeEquipUpdate>(CPP.PatternSearch("48 89 5C 24 ? 57 48 83 EC ? 48 8B F9 48 89 74 24"), FighterMode_Equip_Update);

            Camera.m_updateFuncOrig = engine.CreateHook<Camera.CCameraFreeUpdate>(CPP.PatternSearch("4C 8B DC 55 41 56 49 8D AB 28 FB FF FF"), Camera.CCameraFree_Update);

            HActModule.Init();
            PlayerInput.Init();

            IniSettings.Read();

            engine.EnableHook(dieDancer);
            engine.EnableHook(dieLiveDancer);
            engine.EnableHook(Camera.m_updateFuncOrig);
            engine.EnableHook(m_fighterPreDestroyOrig);
            engine.EnableHook(m_fighterModeEquipUpdateOrig);

            //Replace handshake guard with Ichiban
            IntPtr handshakeGuardModelName = CPP.ResolveRelativeAddress(CPP.PatternSearch("8D 4A 06 E8 ? ? ? ? E8 ? ? ? ? B9 50 17 00 00 E8") + 0x52, 7);
            CPP.PatchMemory(handshakeGuardModelName, System.Text.Encoding.ASCII.GetBytes("c_am_ichiban_tx_on"));


            //dont ask
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = 100;
            timer.AutoReset = false;
            timer.Elapsed += delegate
            {
                string cfcPath = Parless.GetFilePath("data/fighter/command/fighter_command.cfc");
                string propBinPath = Parless.GetFilePath("data/motion/property.bin");
                IsIchibanMovesetEnabled = cfcPath.Contains("Y5Coop") && propBinPath.Contains("Y5Coop");
            };
            timer.Enabled = true;

            OE.LogInfo("Y5 Coop Init End");
        }

        void Reset()
        {
            m_coopPlayerIdx = -1;
            CoopPlayerHandle.UID = 0;
            m_awaitingSpawn = false;
            CoopPlayer = null;
            m_playerSpawnedDoOnce = false;
            CoopPlayerExistTime = 0;
            PlayerInput.DisableInputOverride();
            OnCoopPlayerInvalid();
        }

        void Update()
        {
            if (DebugInput)
            {
                for (int i = 1; i < 10; i++)
                {
                    InputDeviceType type = (InputDeviceType)i;
                    if (PlayerInput.CheckIsThereAnyInput(type))
                    {
                        OE.LogInfo("Input detected on: " + i);
                    }
                }
            }

            IntPtr playerInputDat = InputDeviceData.GetRawData(PlayerInput.Player2InputType);

            bool respawnInput = (Marshal.ReadInt16(playerInputDat) & 512) != 0 || (Marshal.ReadInt16(playerInputDat) & 256) != 0;

            if (respawnInput && m_teleportDelay <= 0 && ActionHActManager.Current.Pointer != IntPtr.Zero)
            {
                DestroyAndRecreatePlayer2();
                m_teleportDelay = 1;
            }

            if (m_teleportDelay > 0)
                m_teleportDelay -= ActionManager.DeltaTime;

            if (CoopPlayerHandle.UID > 0)
            {
                if (!ActionFighterManager.IsFighterPresent(m_coopPlayerIdx))
                {
                    CoopPlayer.Pointer = IntPtr.Zero;
                    Reset();
                    return;
                }

                CoopPlayerDoesntExistTime = 0;
                CoopPlayerExistTime += ActionManager.UnscaledDeltaTime;

                Fighter coopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);

                if (Vector3.Distance(CoopPlayer.Position, ActionFighterManager.GetFighter(0).Position) >= TeleportDistance)
                {
                    DestroyAndRecreatePlayer2();
                    return;
                }

                PlayerInput.Calibrate();
            }
            else
                CoopPlayerDoesntExistTime += ActionManager.UnscaledDeltaTime;

            if (IsDance())
                DanceUpdate();
            else
            {
                if (ActionFighterManager.IsFighterPresent(0))
                {
                    /*
                    if (IsChase())
                        ChaseUpdate();
                    else
                    */
                    NormalUpdate();
                    PlayerAssignmentUpdate();
                }
                else
                {
                    if (IsDance())
                        DanceUpdate();
                }
            }

            uint mission = SequenceManager.MissionID;

            if (mission == m_currentMission && ActionManager.IsLoaded())
                CurrentMissionTime += ActionManager.UnscaledDeltaTime;
            else
            {
                if (m_currentMission != mission)
                    OnMissionChange(m_currentMission, mission);

                CurrentMissionTime = 0;
                m_currentMission = mission;
            }
        }

        private void DanceUpdate()
        {
            Human dancer = new Human();

            bool isLiveBattle = SequenceManager.MissionID == 242;
            bool isDanceBattle = SequenceManager.MissionID == 247;

            if (isDanceBattle)
            {
                dancer = ActionDanceBattleManager.GetDancer(0);
            }
            else if (isLiveBattle)
            {
                //03.05.2025: Live battles prevent progress on story sections

                dancer = ActionPrincessLeagueManager.Player;

                if (dancer.Pointer == IntPtr.Zero)
                    if (LiveBtlPlayer.Current.UID > 0)
                        dancer = LiveBtlPlayer.Current;
                if (dancer.Pointer == IntPtr.Zero)
                    dancer = ActionLiveBattleManager.Current;


                PlayerInput.DisableInputPatches();
            }


            if (!ActionFighterManager.IsFighterPresent(0) && !m_awaitingSpawn)
            {
                float spawnTime = isLiveBattle ? 0.5f : 0.75f;

                if (dancer.Pointer != IntPtr.Zero && CurrentMissionTime > spawnTime)
                {
                    if (AutomaticallyCreatePlayer && CanCreate())
                        CreatePlayer2ForDanceBattle(dancer);
                }
            }

            if (ActionFighterManager.IsFighterPresent(0))
            {
                Fighter coopDancer = ActionFighterManager.GetFighter(0);
                CoopPlayer = coopDancer;

                if (PlayerInput.AllowPlayer1InputCalibration)
                    PlayerInput.OnInputCalibrated(PlayerInput.Player1InputType, true);

                PlayerInput.OnInputCalibrated(PlayerInput.Player2InputType, false);

                Matrix4x4 dancerMtx = dancer.HumanMotion.Matrix;

                if (coopDancer.HumanMotion.CurrentAnimation != dancer.HumanMotion.CurrentAnimation)
                    coopDancer.HumanMotion.NextAnimation = dancer.HumanMotion.CurrentAnimation;

                coopDancer.HumanMotion.SetPosition(dancer.Position + dancerMtx.LeftDirection * 1.1f + -dancerMtx.ForwardDirection * 0.75f);
                coopDancer.HumanMotion.Flags = dancer.HumanMotion.Flags;
                coopDancer.HumanMotion.Mode = dancer.HumanMotion.Mode;
                coopDancer.HumanMotion.AnimationTime = dancer.HumanMotion.AnimationTime;
                coopDancer.HumanMotion.PreviousAnimationTime = dancer.HumanMotion.PreviousAnimationTime;
            }
        }

        private void ChaseUpdate()
        {
            if (m_coopPlayerIdx == -1 && AutomaticallyCreatePlayer)
            {
                m_coopPlayerIdx = 255;

                System.Timers.Timer timer = new System.Timers.Timer();
                timer.Interval = 5600;
                timer.Elapsed += delegate { CreatePlayer2(false); };
                timer.Enabled = true;
                timer.AutoReset = false;

                OE.LogInfo("Spawn Triggered");
            }

            if (m_awaitingSpawn)
            {
                CoopPlayerHandle = new EntityHandle(ActionFighterManager.GetFighter(m_coopPlayerIdx));
                CoopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);
                m_awaitingSpawn = false;
            }

            if (CoopPlayerHandle.IsValid())
            {

            }
        }


        private void OnCoopPlayerSpawned()
        {
            CoopPlayerHandle = new EntityHandle(ActionFighterManager.GetFighter(m_coopPlayerIdx));
            CoopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);

            //ActionFighterManager.GetFighter(0).InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player1InputType));
            CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player2InputType));
            m_awaitingSpawn = false;

            if (PlayerInput.IsPlayer1InputCalibrated)
                PlayerInput.FuckedUpPlayer1WorkAround = true;

            PlayerInput.EnableInputPatches();
            //LoadIchibanResources();
        }

        private void LoadIchibanResources()
        {
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_01");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_02");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_03");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_04");

            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_01_fin");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_02_fin");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_03_fin");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_04_fin");

            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_01_finw");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_02_finw");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_03_finw");
            ActionMotionManager.LoadGMT("p_ich_btl_sud_ath_04_finw");
        }

        private void OnCoopPlayerInvalid()
        {
            m_battleStartRecreatePlayer = false;
            m_btlstDecided = false;
        }

        private void NormalUpdate()
        {
            if (m_awaitingSpawn)
            {
                if (ActionFighterManager.IsFighterPresent(m_coopPlayerIdx))
                {
                    OnCoopPlayerSpawned();
                }
            }
            else if (CoopPlayerHandle.UID == 0 || !CoopPlayerHandle.IsValid() || (m_coopPlayerIdx > -1 && !ActionFighterManager.IsFighterPresent(m_coopPlayerIdx)))
            {
                if (!CanCreate())
                    return;

                if (AutomaticallyCreatePlayer)
                {
                    OE.LogInfo("Automatically creating player2");
                    CreatePlayer2(m_remakePlayer);
                    OE.LogInfo("Player 2 spawned on mission ID:" + SequenceManager.MissionID);
                }
            }
            else
            {

                if (ActionManager.IsPaused())
                    PlayerInput.DisableInputOverride();
                else
                    PlayerInput.EnableInputOverride();

                if (IsBattle())
                {
                    SetPlayer2Commandset();

                    if (ActionFighterManager.Player.HumanMotion.CurrentAnimation.ToString().Contains("btlst"))
                    {
                        if (!m_battleStartRecreatePlayer)
                        {
                            DestroyAndRecreatePlayer2();
                            m_battleStartRecreatePlayer = true;
                            ActionFighterManager.SetPlayer(m_coopPlayerIdx);
                        }
                    }

                    if (!m_btlstDecided)
                    {
                        if (CoopPlayer != null && ActionFighterManager.IsFighterPresent(m_coopPlayerIdx))
                        {

                            if (CoopPlayer.ModeManager.Current.Name == "WaitStartMotion")
                            {
                                unsafe
                                {
                                    MotionID[] p2Anims = new MotionID[]
                                    {
                                        MotionID.E_SUG_CMB_b_01,
                                        MotionID.E_SUG_CMB_b_02,
                                        MotionID.E_SUG_CMB_a_01,
                                    };

                                    FighterMode mode = CoopPlayer.ModeManager.Current;
                                    int* battleStartAnimationID = (int*)(mode.Pointer + 0x90);
                                    *battleStartAnimationID = (int)p2Anims[new Random().Next(0, p2Anims.Length)];

                                    m_btlstDecided = true;
                                }
                            }
                        }
                    }
                }
            }
        }


        private void SetPlayer2Commandset()
        {
            if (string.IsNullOrEmpty(CoopPlayerCommandset))
                return;

            //another property bin/cfc changing mod active
            if (CoopPlayerCommandset == "ichiban" && !IsIchibanMovesetEnabled)
                return;

             CoopPlayer.ModeManager.SetCommandset(CoopPlayerCommandset);
        }

        public bool CanCreate()
        {
            return !m_dontRespawnPlayerThisMissionDoOnce && !BlacklistedMissions.Contains((int)SequenceManager.MissionID);
        }

        public static bool IsBattle()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 400 || mission == 408;
        }

        public static bool IsChase()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 409 || mission == 500;
        }

        public static bool IsDance()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 242 || mission == 247;
        }

        public static bool IsHunting()
        {
            return SequenceManager.MissionID == 405;
        }

        public void OnMissionChange(uint oldMission, uint newMission)
        {
            m_dontRespawnPlayerThisMissionDoOnce = false;

            if (oldMission == 300 && newMission == 400)
            {
                Fighter coopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);

                //Random encounter?
                if (coopPlayer.Pointer != IntPtr.Zero)
                {
                    Fighter main = ActionFighterManager.GetFighter(0);

                    OE.LogInfo("Random encounter");
                    coopPlayer.HumanMotion.Flags = 0;
                    coopPlayer.HumanMotion.Mode = 0;
                    coopPlayer.HumanMotion.SetPosition(main.Position);
                    coopPlayer.HumanMotion.Flags = main.HumanMotion.Flags;
                    coopPlayer.HumanMotion.Mode = main.HumanMotion.Mode;
                }
            }
        }

        public static void PlayerAssignmentUpdate()
        {
            //Set primary player based on who is aiming
            if (IsHunting())
            {
                Fighter main = ActionFighterManager.GetFighter(0);
                Fighter coopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);

                if ((main.InputFlags & 0x100000) != 0)
                    ActionFighterManager.SetPlayer(0);
                else if ((coopPlayer.InputFlags & 0x100000) != 0)
                    ActionFighterManager.SetPlayer(m_coopPlayerIdx);
                else
                    ActionFighterManager.SetPlayer(0);
            }
            else
            {
                ActionFighterManager.SetPlayer(0);
                return;
            }
        }

        public void InputThread()
        {
            IntPtr controllerDat = InputDeviceData.GetRawData(InputDeviceType.Controller);

            while (true)
            {
                if (OE.IsKeyHeld(VirtualKey.LeftShift))
                    if (OE.IsKeyDown(VirtualKey.P))
                        CreatePlayer2(false);

#if DEBUG

                if (OE.IsKeyHeld(VirtualKey.LeftControl))
                    if (OE.IsKeyDown(VirtualKey.R))
                    {
                        IniSettings.Read();
                        OE.LogInfo("Reloaded ini settings");
                    }

                if (OE.IsKeyHeld(VirtualKey.LeftControl))
                {
                    if (OE.IsKeyDown(VirtualKey.Z))
                        if (CoopPlayer != null && ActionFighterManager.IsFighterPresent(m_coopPlayerIdx))
                            CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
                        //FixupTest();
                }
#endif
            }

        }

        private void DestroyPlayer2()
        {
            PlayerInput.ResetPlayer2Input();
            ActionFighterManager.DestroyFighter(m_coopPlayerIdx);
            Reset();
        }

        private void DestroyAndRecreatePlayer2()
        {
            if (m_coopPlayerIdx >= 0 && ActionFighterManager.IsFighterPresent(m_coopPlayerIdx) && !m_awaitingSpawn)
            {
                DestroyPlayer2();
                m_remakePlayer = true;

            }
        }

        private void CreatePlayer2(bool recreate)
        {
            IntPtr inputUpdateAddr = Y5Lib.Unsafe.CPP.PatternSearch("49 63 C7 49 0F 45 D6 48 69 C8 ? ? ? ? 48 03 CB E8 ? ? ? ? 8B 84 24 D0 01 00 00");

            if (inputUpdateAddr != IntPtr.Zero) ;
            //Y5Lib.Unsafe.CPP.PatchMemory(inputUpdateAddr, 0xB8, 0x9, 0x0, 0x0, 0x0, 0x90, 0x90);

            DisposeInfo p1Dispose = ActionFighterManager.Player.Dispose;

            DisposeInfo inf = new DisposeInfo();
            inf.N000002CE = 3633;
            inf.N00000520 = 1424508304;
            inf.N00000521 = 11;
            inf.modelName.Set(GetModelForPlayer2());
            inf.N00000549 = 300;
            inf.N0000054A = 100;
            inf.N00004547 = -1;
            inf.N0000454A = 87;
            inf.FighterType = p1Dispose.FighterType;
            inf.N0000454B = 10;
            inf.Voicer = 87;
            inf.N00004552 = 255;


            NPCType playerType = inf.FighterType;

            if (!m_remakePlayer)
            {
                if (IsChase())
                    inf.spawnPosition = ActionFighterManager.Player.Position + -ActionFighterManager.Player.HumanMotion.Matrix.ForwardDirection;
                else
                    //  inf.spawnPosition = ActionFighterManager.Player.Position + -ActionFighterManager.Player.HumanMotion.Matrix.LeftDirection;

                    inf.spawnPosition = ActionFighterManager.Player.Position + new Vector3(1, 0, 0);
            }
            else
            {
                if (IsBattle())
                    inf.spawnPosition = ActionFighterManager.Player.Position + ActionFighterManager.Player.HumanMotion.Matrix.LeftDirection * 1.2f;
                else
                    inf.spawnPosition = ActionFighterManager.Player.Position + -ActionFighterManager.Player.HumanMotion.Matrix.ForwardDirection * 0.65f;
            }


            inf.N0000054E = p1Dispose.N0000054E;
            inf.battleStartAnim.Set("P_btlst_stand_poki");
            inf.N0000453C = -1;

            int idx = ActionFighterManager.SpawnCharacter(inf);

            if (idx < 0)
                OE.LogError("Couldn't spawn co-op player!");
            else
            {
                m_coopPlayerIdx = idx;
                m_awaitingSpawn = true;
                ActionFighterManager.SetPlayer(0);

                OE.LogInfo("Waiting co-op player to spawn...");
            }
        }

        private void CreatePlayer2ForDanceBattle(Human haruka)
        {
            IntPtr inputUpdateAddr = CPP.PatternSearch("49 63 C7 49 0F 45 D6 48 69 C8 ? ? ? ? 48 03 CB E8 ? ? ? ? 8B 84 24 D0 01 00 00");

            DisposeInfo inf = new DisposeInfo();
            inf.N000002CE = 64904;
            inf.N00000520 = 52;
            inf.N00000521 = 11;
            inf.modelName.Set(GetModelForPlayer2DanceBattle(haruka));
            inf.N00000549 = 300;
            inf.N0000054A = 100;
            inf.N00004547 = -1;
            inf.N0000454A = 4;
            inf.FighterType = NPCType.Mannequin;
            inf.N0000454B = 10;
            inf.Voicer = 87;
            inf.N00004552 = 25;

            inf.spawnPosition = haruka.Position; // new Vector3(76.92f, 0f, -16.21f);
            inf.N0000054E = (uint)haruka.HumanMotion.GetAngleY();

            inf.battleStartAnim.Set("eMID_NONE");
            inf.N0000453C = -1;

            m_coopPlayerIdx = ActionFighterManager.SpawnCharacter(inf);
            m_awaitingSpawn = true;
        }

        private static string GetModelForPlayer2()
        {
            string playerModel = ActionFighterManager.Player.Model;

            if (playerModel.Contains("naked"))
                return "c_cm_ichiban_naked";

            if (playerModel.Contains("saejima_g"))
                return "c_cm_ichiban_g";

            if(playerModel == "c_cw_haruka_w")
                return "c_cm_ichiban_tx_on";

            if (playerModel.Contains("haruka"))
                return "c_cm_ichiban_haruka";

            switch (playerModel)
            {
                default:
                    return "c_cm_ichiban";
                case "c_cm_saejima_mtg":
                    return "c_cm_ichiban_mtg";
                case "c_cm_kiryu":
                    return "c_cm_ichiban";
                case "c_cm_kiryu_tx_off":
                    return "c_cm_ichiban_tx_off";
                case "c_cm_kiryu_tx_on":
                    return "c_cm_ichiban_tx_on";
                case "c_cm_kiryu_tx_caba":
                    return "c_cm_ichiban_tx_caba";
            }
        }

        private static string GetModelForPlayer2DanceBattle(Human dancer)
        {
            if (SequenceManager.MissionID == 242)
                return "c_cm_ichiban_haruka_lesson";
            else
                return "c_cm_ichiban_haruka";
        }

        public void FixupTest()
        {
            m_dontRespawnPlayerThisMissionDoOnce = true;
            DestroyPlayer2();
            //engine.DisableHooks();
        }
    }
}
