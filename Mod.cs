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

        public static Mod Instance;

        public static HookEngine engine = new HookEngine();

        public static Fighter CoopPlayer = null;
        public static EntityHandle CoopPlayerHandle;

        public static int CoopPlayerIdx = -1;
        public static int m_oldCoopIdx = -1;
        private static bool m_awaitingSpawn = false;

        private static bool m_playerSpawnedDoOnce = false;

        public static bool AllyMode = false;

        public static float CoopPlayerExistTime = 0;
        public static float CoopPlayerDoesntExistTime = 0;
        public static float CurrentMissionTime = 0;

        public static bool AutomaticallyCreatePlayer = true;
        public static bool DestroyP2OnCCC = false;
        public static int[] BlacklistedMissions = new int[0];

        public static float TeleportDistance = 20;

        public static string CoopPlayerCommandset = "";

        //Fighter command and property.bin belongs to our mod
        public static bool IsIchibanMovesetEnabled;

        public static bool DebugInput = true;

        private static bool m_spawningWithDelay = false;
        private static float m_spawnDelay = 0;
        private static float m_teleportDelay = 0;


        private static int m_playerID = -1;

        private static bool m_remakePlayer = false;
        private static bool m_remakePlayerPreservePosition = false;
        private static Vector3 m_remakePlayerPreservePos;
        private static ushort m_remakePlayerPreserveAngle;

        public static bool DontRespawnPlayerThisMissionDoOnce = false;
        private static bool m_battleStartRecreatePlayer = false;
        private static bool m_btlstDecided = false;
        public static MotionID ChosenBtlst;
        private static uint m_currentMission;
        private delegate IntPtr CActionFighterManagerGetFighterByUID(IntPtr fighterMan, uint serial);

        MotionID[] p2BattleStartAnims = new MotionID[]
{
                                        MotionID.E_SUG_CMB_b_01,
                                        MotionID.E_SUG_CMB_b_02,
                                        MotionID.E_SUG_CMB_a_01,
};

        public override void OnModInit()
        {
            base.OnModInit();

            Instance = this;

            OE.LogInfo("Y5 Coop Init Start");

            var y5lib = AppDomain.CurrentDomain.GetAssemblies().
           SingleOrDefault(assembly => assembly.GetName().Name == "Y5Lib.NET");

            if (y5lib.GetName().Version < new Version(1, 0, 1, 7))
            {
                MessageBox(IntPtr.Zero, "Current version of Intertwined Fates requires Y5Lib version 0.17 and greater. Please update Y5Lib from Libraries tab on the top left of Shin Ryu Mod Manager.\nRefer to INSTALLATION segment in NexusMods description if you are unsure of what to do.", "Need Update", 0);
                Environment.Exit(0);
            }

            CombatPatches.Init();

            OE.RegisterJob(Update, (JobPhase)10);

            Thread thread = new Thread(InputThread);
            thread.Start();

            m_origGetEntityByUID = engine.CreateHook<CActionFighterManagerGetFighterByUID>((IntPtr)0x1408D33C0, ActionEntityManager_GetEntityByUID);

            Camera.m_updateFuncOrig = engine.CreateHook<Camera.CCameraFreeUpdate>(CPP.PatternSearch("4C 8B DC 55 41 56 49 8D AB 28 FB FF FF"), Camera.CCameraFree_Update);

            IniSettings.Read();

            HActModule.Init();
            PlayerInput.Init();

            FighterPatches.Init();
            ActionFighterManagerPatches.Init();
            MGDrivePatches.Init();
            MGHandshakePatches.Init();

            engine.EnableHook(Camera.m_updateFuncOrig);
            engine.EnableHook(m_origGetEntityByUID);

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

        private static CActionFighterManagerGetFighterByUID m_origGetEntityByUID;
        IntPtr ActionEntityManager_GetEntityByUID(IntPtr entityManager, uint serial)
        {
            if (serial == 0xBEEF)
            {
                return CoopPlayer.Pointer;
            }

            return m_origGetEntityByUID(entityManager, serial);
        }

        public static void Reset()
        {
            CoopPlayerIdx = -1;
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

            HActModule.Update();

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

            if (!AllyMode)
            {
                IntPtr playerInputDat = InputDeviceData.GetRawData(PlayerInput.Player2InputType);

                bool respawnInput = (Marshal.ReadInt16(playerInputDat) & 512) != 0 || (Marshal.ReadInt16(playerInputDat) & 256) != 0;

                if (respawnInput && m_teleportDelay <= 0 && ActionHActManager.Current.Pointer == IntPtr.Zero && !ActionManager.IsPaused())
                {
                    if (!IsDance() && !IsDrive())
                    {
                        DestroyAndRecreatePlayer2();
                        m_teleportDelay = 1;
                    }
                }
            }

            if (m_teleportDelay > 0)
                m_teleportDelay -= ActionManager.DeltaTime;

            if (CoopPlayerHandle.UID > 0)
            {
                if (!ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                {
                    CoopPlayer.Pointer = IntPtr.Zero;
                    Reset();
                    return;
                }

                CoopPlayerDoesntExistTime = 0;
                CoopPlayerExistTime += ActionManager.UnscaledDeltaTime;

                Fighter coopPlayer = ActionFighterManager.GetFighter(CoopPlayerIdx);
                CoopPlayer = coopPlayer;

                if (!HActModule.IsHAct)
                {
                    CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player2InputType));

                    if (CoopPlayerIdx > 0)
                        ActionFighterManager.GetFighter(0).InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player1InputType));
                }


                if (Vector3.Distance(CoopPlayer.Position, ActionFighterManager.GetFighter(0).Position) >= TeleportDistance)
                {
                    DestroyAndRecreatePlayer2();
                    return;
                }

                if (!AllyMode)
                    PlayerInput.Calibrate();
            }
            else
                CoopPlayerDoesntExistTime += ActionManager.UnscaledDeltaTime;

            int playerID = Player.GetCurrentID();

            if (m_playerID != playerID)
            {
                OnPlayerIDChange(m_playerID, playerID);
                m_playerID = playerID;
            }

            if (IsDance())
                DanceUpdate();
            else
            {
                if (ActionFighterManager.IsFighterPresent(0))
                {
                    NormalUpdate();

                    if (IsChase())
                        ChaseUpdate();

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


        private void OnPlayerIDChange(int oldID, int newID)
        {
            OE.LogInfo("Player ID change: From " + oldID + " to: " + newID);

            if (newID == 4)
                CombatPatches.OnStartBestGirl();
            else
                CombatPatches.OnEndBestGirl();
        }
        private void ChaseUpdate()
        {
            if (ActionHActManager.Current.Pointer != IntPtr.Zero)
                return;

            if (CurrentMissionTime > 0.1f && CurrentMissionTime < 10)
            {
                if (CoopPlayer != null)
                {
                    Fighter player = ActionFighterManager.GetFighter(0);

                    float dist = Vector3.Distance(CoopPlayer.Position, player.Position);

                    if (dist < 0.1f)
                    {
                        OE.LogInfo("Player 2 accidentally spawned too close to player 1 on chase. Forcefully repositioning");
                        CoopPlayer.HumanMotion.SetPosition(player.Position + (-player.HumanMotion.Matrix.ForwardDirection * 0.3f));
                    }
                }
            }
        }

        private void DanceUpdate()
        {
            if (m_awaitingSpawn)
            {
                CoopPlayerHandle = new EntityHandle(ActionFighterManager.GetFighter(CoopPlayerIdx));
                CoopPlayer = ActionFighterManager.GetFighter(CoopPlayerIdx);
                m_awaitingSpawn = false;

                if (ActionLiveBattleManager.Current.Pointer != IntPtr.Zero)
                    ActionLiveBattleManager.Current.HumanMotion.SetPosition(ActionLiveBattleManager.Current.Position + -ActionLiveBattleManager.Current.HumanMotion.Matrix.LeftDirection);
            }

            if (ActionPrincessLeagueManager.Player.Pointer != IntPtr.Zero)
            {
                ActionPrincessLeagueManager.Player.HumanMotion.SetPosition(ActionPrincessLeagueManager.Player.Position + -ActionPrincessLeagueManager.Player.HumanMotion.Matrix.LeftDirection * 0.5f);
            }

            Human dancer = new Human();

            bool isLiveBattle = SequenceManager.MissionID == 242;
            bool isDanceBattle = SequenceManager.MissionID == 247;

            if (isDanceBattle)
            {
                dancer = ActionDanceBattleManager.GetDancer(0);

                if (dancer.Pointer == IntPtr.Zero)
                    dancer = ActionDanceEventManager.Player;

                if (!AllyMode)
                    PlayerInput.EnableInputPatches();
            }
            else if (isLiveBattle)
            {
                dancer = ActionPrincessLeagueManager.Player;

                if (dancer.Pointer == IntPtr.Zero)
                    if (LiveBtlPlayer.Current.UID > 0)
                        dancer = LiveBtlPlayer.Current;
                if (dancer.Pointer == IntPtr.Zero)
                    dancer = ActionLiveBattleManager.Current;

                if (!AllyMode)
                    PlayerInput.DisableInputPatches();
            }


            if (!ActionFighterManager.IsFighterPresent(0) && !m_awaitingSpawn)
            {
                float spawnTime = isLiveBattle ? 0.75f : 0.25f;

                if (dancer.Pointer != IntPtr.Zero && CurrentMissionTime > spawnTime)
                {
                    if (AutomaticallyCreatePlayer && CanCreate())
                        if (!(isLiveBattle && ActionHActManager.Current.Pointer != IntPtr.Zero))
                            CreatePlayer2ForDanceBattle(dancer);
                }
            }

            if (ActionFighterManager.IsFighterPresent(0))
            {

                if (isLiveBattle && ActionHActManager.Current.Pointer != IntPtr.Zero)
                {
                    //Okay Ichiban, you had your moment
                    ActionFighterManager.DestroyFighter(0);
                }

                Fighter coopDancer = ActionFighterManager.GetFighter(0);
                CoopPlayer = coopDancer;


                if (!AllyMode)
                {
                    if (PlayerInput.AllowPlayer1InputCalibration)
                        PlayerInput.OnInputCalibrated(PlayerInput.Player1InputType, true);

                    PlayerInput.OnInputCalibrated(PlayerInput.Player2InputType, false);
                }

                Matrix4x4 dancerMtx = dancer.HumanMotion.Matrix;

                if (coopDancer.HumanMotion.CurrentAnimation != dancer.HumanMotion.CurrentAnimation)
                    coopDancer.HumanMotion.NextAnimation = dancer.HumanMotion.CurrentAnimation;

                if (!isLiveBattle)
                    coopDancer.HumanMotion.SetPosition(dancer.Position + dancerMtx.LeftDirection * 1.1f + -dancerMtx.ForwardDirection * 0.75f);
                else
                    coopDancer.HumanMotion.SetPosition(dancer.Position + dancerMtx.LeftDirection * 0.5f);

                coopDancer.HumanMotion.Flags = dancer.HumanMotion.Flags;
                coopDancer.HumanMotion.Mode = dancer.HumanMotion.Mode;
                coopDancer.HumanMotion.AnimationTime = dancer.HumanMotion.AnimationTime;
                coopDancer.HumanMotion.PreviousAnimationTime = dancer.HumanMotion.PreviousAnimationTime;
            }
        }

        private void OnCoopPlayerSpawned()
        {
            CoopPlayerHandle = new EntityHandle(ActionFighterManager.GetFighter(CoopPlayerIdx));
            CoopPlayer = ActionFighterManager.GetFighter(CoopPlayerIdx);
            m_awaitingSpawn = false;


            if (!AllyMode)
            {
                if (!HActModule.IsHAct)
                {
                    ActionFighterManager.GetFighter(0).InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player1InputType));
                    CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(PlayerInput.Player2InputType));
                }

                if (PlayerInput.IsPlayer1InputCalibrated)
                    PlayerInput.FuckedUpPlayer1WorkAround = true;

                PlayerInput.EnableInputPatches();
            }
        }

        public static bool IsCoopPlayerPresent()
        {
            return CoopPlayerIdx != -1 && ActionFighterManager.IsFighterPresent(CoopPlayerIdx);
        }

        private static void OnCoopPlayerInvalid()
        {
            m_battleStartRecreatePlayer = false;
            m_btlstDecided = false;
        }

        private void NormalUpdate()
        {
            if (m_spawningWithDelay)
                m_spawnDelay -= ActionManager.DeltaTime;

            if (m_awaitingSpawn)
            {
                if (ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                {
                    OnCoopPlayerSpawned();
                }
            }
            else if (CoopPlayerHandle.UID == 0 || !CoopPlayerHandle.IsValid() || (CoopPlayerIdx > -1 && !ActionFighterManager.IsFighterPresent(CoopPlayerIdx)))
            {
                if (!CanCreate())
                    return;

                if (AutomaticallyCreatePlayer)
                {
                    if (!m_remakePlayer || (m_remakePlayer && !ActionFighterManager.IsFighterPresent(m_oldCoopIdx)))
                    {
                        if (!m_remakePlayer && !m_spawningWithDelay)
                        {
                            bool shouldSpawnWithDelay = (IsBattle() || IsFreeroam()) && !IsEncounter();

                            if (IsChase() || IsDance())
                                shouldSpawnWithDelay = false;

                            //Spawn player with a slight delay
                            if (shouldSpawnWithDelay && !m_spawningWithDelay)
                            {
                                m_spawnDelay = 0.25f;
                                m_spawningWithDelay = true;

                                OE.LogInfo("Spawning player 2 with " + m_spawnDelay + " seconds of delay.");
                            }
                        }

                        if (m_spawnDelay > 0 && m_spawningWithDelay)
                            return;


                        OE.LogInfo("Automatically creating player2");

                        CreatePlayer2(m_remakePlayer);
                        OE.LogInfo("Player 2 spawned on mission ID:" + SequenceManager.MissionID + " Stage ID: " + ActionStageManager.StageID);

                        m_remakePlayer = false;
                        m_spawningWithDelay = false;
                    }
                }
            }
            else
            {

                if (!AllyMode)
                {
                    if (ActionManager.IsPaused())
                        PlayerInput.DisableInputOverride();
                    else
                        PlayerInput.EnableInputOverride();
                }

                if (IsBattle())
                {
                    SetPlayer2Commandset();

                    if (!AllyMode)
                    {
                        if (ActionFighterManager.Player.HumanMotion.CurrentAnimation.ToString().Contains("btlst"))
                        {
                            if (!m_battleStartRecreatePlayer)
                            {
                                if (!IsEncounter())
                                    DestroyAndRecreatePlayer2();
                                else
                                    CoopPlayer.HumanMotion.NextAnimation = p2BattleStartAnims[new Random().Next(0, p2BattleStartAnims.Length)];
                                m_battleStartRecreatePlayer = true;
                                ActionFighterManager.SetPlayer(CoopPlayerIdx);
                            }
                        }

                    }
                    if (CoopPlayer != null && ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                    {
                        string modeName = CoopPlayer.ModeManager.Current.Name;
                        bool isBtlSt = modeName == "WaitStartMotion" || modeName == "NoCutEncountStartMotion";
                        string currentHactName = ActionHActManager.Current.Name;
                        bool isStartHact = currentHactName.Contains("auth") || currentHactName.Contains("btlst");

                        if (isBtlSt)
                        {
                            unsafe
                            {
                                FighterMode mode = CoopPlayer.ModeManager.Current;
                                int* battleStartAnimationID = (int*)(mode.Pointer + 0x90);
                                *battleStartAnimationID = (int)ChosenBtlst;

                                m_btlstDecided = true;
                            }
                        }
                    }
                }
                else
                {
                    if (DestroyP2OnCCC && ActionCCCManager.isActive && CoopPlayer != null && ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                    {
                        DestroyPlayer2();
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
            if (DestroyP2OnCCC && ActionCCCManager.isActive)
                return false;

            if (AllyMode)
            {
                if (IsHunting())
                    return false;

                uint mission = SequenceManager.MissionID;

                if (mission == 300 || mission == 409 || mission == 403 || mission == 410)
                    return false;
            }

            return !DontRespawnPlayerThisMissionDoOnce && !BlacklistedMissions.Contains((int)SequenceManager.MissionID);
        }


        public static bool IsEncounter()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 400;
        }

        public static bool IsFreeroam()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 300;
        }

        public static bool IsBattle()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 400 || mission == 401 || mission == 408;
        }

        public static bool IsHarukaBattle()
        {
            return m_playerID == 4 && IsBattle();
        }

        public static bool IsChase()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 409 || mission == 500;
        }

        public static bool IsDrive()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 600;
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
            DontRespawnPlayerThisMissionDoOnce = false;

            if (oldMission == 300 && newMission == 400)
            {
                if (CoopPlayerIdx >= 0 && ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                {
                    Fighter coopPlayer = ActionFighterManager.GetFighter(CoopPlayerIdx);

                    //Random encounter?
                    if (coopPlayer.Pointer != IntPtr.Zero)
                    {
                        Fighter main = ActionFighterManager.GetFighter(0);

                        //come to me pal, we are in a fight!
                        if (Vector3.Distance(main.Position, coopPlayer.Position) >= 10)
                        {
                            DestroyAndRecreatePlayer2();
                        }

                        OE.LogInfo("Our co-op buddies have entered an encounter battle...");
                    }
                }
            }
        }

        public static void PlayerAssignmentUpdate()
        {
            //Set primary player based on who is aiming
            if (IsHunting())
            {
                Fighter main = ActionFighterManager.GetFighter(0);
                Fighter coopPlayer = ActionFighterManager.GetFighter(CoopPlayerIdx);

                if ((main.InputFlags & 0x100000) != 0)
                    ActionFighterManager.SetPlayer(0);
                else if ((coopPlayer.InputFlags & 0x100000) != 0)
                    ActionFighterManager.SetPlayer(CoopPlayerIdx);
                else
                    ActionFighterManager.SetPlayer(0);
            }
            else
            {
                //Haruka Sawamura
                if (IsHarukaBattle())
                {
                    if (CoopPlayer != null && ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                        ActionFighterManager.SetPlayer(CoopPlayerIdx);
                    else
                        ActionFighterManager.SetPlayer(0);
                }
                else
                {
                    ActionFighterManager.SetPlayer(0);
                }

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
                        if (CoopPlayer != null && ActionFighterManager.IsFighterPresent(CoopPlayerIdx))
                            CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));

                    if (OE.IsKeyDown(VirtualKey.H))
                    {
                        ActionMotionManager.LoadImportantResources(true);
                        OE.LogInfo("load important resources");
                    }
                    //FixupTest();
                }
#endif
            }

        }

        public static void DestroyPlayer2()
        {
            m_oldCoopIdx = CoopPlayerIdx;

            PlayerInput.ResetPlayer1Input();

            if (!AllyMode)
                PlayerInput.ResetPlayer2Input();

            ActionFighterManager.DestroyFighter(CoopPlayerIdx);
            Reset();
        }

        private void DestroyAndRecreatePlayer2(bool preservePosition = false)
        {
            if (!m_remakePlayer && CoopPlayerIdx >= 0 && ActionFighterManager.IsFighterPresent(CoopPlayerIdx) && !m_awaitingSpawn)
            {
                OE.LogInfo("Destroying and immediately remaking player 2.");

                DestroyPlayer2();
                m_remakePlayer = true;
            }
        }

        private void CreatePlayer2(bool recreate)
        {
            OE.LogInfo("Automatically creating player2");

            DisposeInfo p1Dispose = ActionFighterManager.Player.Dispose;

            NPCType playerType;

            if (AllyMode)
                playerType = NPCType.Npc;
            else
                playerType = p1Dispose.FighterType;

            DisposeInfo inf = new DisposeInfo();
            inf.modelName.Set(GetModelForPlayer2());

            if (!m_btlstDecided)
            {
                ChosenBtlst = p2BattleStartAnims[new Random().Next(0, p2BattleStartAnims.Length)];
                m_btlstDecided = true;
            }

            inf.battleStartAnim.Set(ChosenBtlst.ToString());

            if (AllyMode)
            {
                inf.N0000051D = 0;
                inf.N000002C1 = -27517;
                inf.N00001F51 = 16515;
                inf.N0000051E = 500838487254548003;
                inf.N0000051F = 2206420355;
                inf.N000002CE = 1652788355;
                inf.N00000520 = 26499;
                inf.N000002BD = 65540;
                inf.N00000521 = 0;
                inf.N000002BF = 3722266699;
                inf.N00000547 = 8242266182453912832;
                inf.N00000548 = 7018143632393335393;
                inf.N00000549 = 966263784;
                inf.N000002D3 = 0;
                inf.N0000054A = 0;
                inf.N00004547 = 255;
                inf.N00004545 = 0;
                inf.N0000454A = 255;
                inf.N0000454B = 10;
                inf.Voicer = 178;
                inf.N00004552 = 255;
                inf.N00004556 = 0;
                inf.N0000455D = 12032;
                inf.N00003ED8 = 54089;
                inf.N0000054E = 0xBE16;
            }
            else
            {
                inf.N000002CE = 3633;
                inf.N00000520 = 1424508304;
                inf.N00000521 = 11;
                inf.N0000453C = -1;
                inf.N00000549 = 300;
                inf.N0000054A = 100;
                inf.N00004547 = -1;
                inf.N0000454A = 87;
                inf.N0000454B = 10;
                inf.Voicer = 87;
                inf.N00004552 = 255;
            }


            if (!m_remakePlayer)
            {
                if (IsChase())
                    inf.spawnPosition = ActionFighterManager.Player.Position + ActionFighterManager.Player.HumanMotion.Matrix.ForwardDirection * 2 + (-ActionFighterManager.Player.HumanMotion.Matrix.LeftDirection * 0.5f);
                else
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


            int idx = ActionFighterManager.SpawnCharacter(inf);

            if (idx < 0)
                OE.LogError("Couldn't spawn co-op player!");
            else
            {
                CoopPlayerIdx = idx;
                m_awaitingSpawn = true;
                ActionFighterManager.SetPlayer(0);

                OE.LogInfo("Waiting co-op player to spawn...");
            }
        }

        private void CreatePlayer2ForDanceBattle(Human haruka)
        {
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

            CoopPlayerIdx = ActionFighterManager.SpawnCharacter(inf);
            m_awaitingSpawn = true;
        }

        public static string GetModelForPlayer2()
        {
            string playerModel = ActionFighterManager.Player.Model;
            int stageID = ActionStageManager.StageID;

            //haruka idol maps where he should be in his business attire
            if (stageID == 131 || stageID == 149)
                return "c_cm_ichiban_tx_on";

            if (playerModel.Contains("naked"))
                return "c_cm_ichiban_naked";


            if (playerModel.Contains("sinada"))
                return "c_cm_ichiban_sode";

            if (playerModel.Contains("saejima_g"))
                return "c_cm_ichiban_g";

            if (playerModel == "c_cw_haruka_w")
                return "c_cm_ichiban_tx_on";

            if (playerModel == "c_aw_haruka_lesson")
                return "c_cm_ichiban_haruka";

            if (playerModel.Contains("haruka"))
            {
                if (IsDance())
                    return "c_cm_ichiban_haruka";
                else
                    return "c_cm_ichiban";
            }

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
            string dancerModel = dancer.Model.Name;

            if (dancerModel.Contains("akiyama"))
                return "c_cm_ichiban_sode";

            if (ActionPrincessLeagueManager.Player.Pointer != IntPtr.Zero)
            {
                return "c_am_ichiban_idol_mg";
            }

            if (SequenceManager.MissionID == 242)
                return "c_am_ichiban_haruka_mg";
            else
                return "c_cm_ichiban_haruka";
        }

        public void FixupTest()
        {
            DontRespawnPlayerThisMissionDoOnce = true;
            DestroyPlayer2();
            //engine.DisableHooks();
        }
    }
}
