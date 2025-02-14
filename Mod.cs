using MinHook;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Y5Lib;

//Crash 1: 140f69405
namespace Y5Coop
{
    //141D9D980 dance battle player when on that mission
    public unsafe class Mod : Y5Mod
    {

        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

        private delegate void* DeviceListener_GetInput(void* a1);
        private delegate void FighterController_InputUpdate(void* a1);
        private delegate bool Fighter_SomeMysteriousInputUpdate(void* a1, ulong arg1, ulong arg2);

        public static string ModPath;

        //140A3320E - process **CONTROLLER** keypress, nop : no more controller input
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

        private static bool m_remakePlayer;

        Fighter_SomeMysteriousInputUpdate test;


        private delegate ulong DancerDestructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5);

        DancerDestructor dieDancer;
        ulong Dancer_Destructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5)
        {
            //We must send a destroy signal to ActionFighterManager precisely at this function
            //Otherwise, our game will get stuck!
            //How the fuck does this happen? How did i even fix this?
            //..i have no idea!
            if (m_coopPlayerIdx > -1)
            {
                ActionFighterManager.DestroyFighter(m_coopPlayerIdx);
                Reset();
            }
            return dieDancer(dancer, idk, idk2, idk3, idk4, idk5);
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

            IntPtr fighterInputUpdateAddr = Y5Lib.Unsafe.CPP.PatternSearch("40 56 41 57 48 81 EC ? ? ? ? C5 78 29 44 24 60");
            IntPtr fighterInputUpdate2Addr = Y5Lib.Unsafe.CPP.PatternSearch("48 83 EC ? 8B 91 30 34 00 00 0F BA E2 ? 72 ?");

            if (fighterInputUpdateAddr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            if (fighterInputUpdate2Addr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update 2 function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update 2 function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            PlayerInput.m_controllerInputUpdate = engine.CreateHook<PlayerInput.FighterController_InputUpdate>(fighterInputUpdateAddr, PlayerInput.FighterController__InputUpdate);
            test = engine.CreateHook<Fighter_SomeMysteriousInputUpdate>(fighterInputUpdate2Addr, Test);
            dieDancer = engine.CreateHook<DancerDestructor>((IntPtr)0x1404335E0, Dancer_Destructor);

            Camera.m_updateFuncOrig = engine.CreateHook<Camera.CCameraFreeUpdate>(Y5Lib.Unsafe.CPP.PatternSearch("4C 8B DC 55 41 56 49 8D AB 28 FB FF FF"), Camera.CCameraFree_Update);

            HActModule.Init();
            PlayerInput.Init();

            engine.EnableHooks();
            IniSettings.Read();


            OE.LogInfo("Y5 Coop Init End");
        }

        //If you do not route this to player1, game freezes when interacting with CCC
        bool Test(void* fighter, ulong arg1, ulong arg2)
        {
            return test.Invoke((void*)ActionFighterManager.GetFighter(0).Pointer, arg1, arg2);
        }

        void Reset()
        {
            m_coopPlayerIdx = -1;
            CoopPlayerHandle.UID = 0;
            m_awaitingSpawn = false;
            CoopPlayer = null;
            m_playerSpawnedDoOnce = false;
            CoopPlayerExistTime = 0;
        }

        void Update()
        {
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

                if(Vector3.Distance(CoopPlayer.Position, ActionFighterManager.GetFighter(0).Position) >= TeleportDistance)
                {
                    DestroyAndRecreatePlayer2();
                    return;
                    //CoopPlayer.HumanMotion.SetPosition(ActionFighterManager.GetFighter(0).Position);
                }

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
            {
                CurrentMissionTime += ActionManager.UnscaledDeltaTime;
            }
            else
            {
                if(m_currentMission != mission)
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
                //Steam only 
                long* dancePlrHaruka = (long*)0x141D9D980;
                dancer = new Human() { Pointer = (IntPtr)(*dancePlrHaruka) };
            }
            else if(isLiveBattle)
            {
                dancer = ActionPrincessLeagueManager.Player;

               if(dancer.Pointer == IntPtr.Zero)
                    if(LiveBtlPlayer.Current.UID > 0)
                        dancer = LiveBtlPlayer.Current;
            }


            if (!ActionFighterManager.IsFighterPresent(0) && !m_awaitingSpawn)
            {
                float spawnTime = isLiveBattle ? 0.5f : 0.75f;

                if (dancer.Pointer != IntPtr.Zero && CurrentMissionTime > spawnTime)
                {
                    if (AutomaticallyCreatePlayer)
                        CreatePlayer2ForDanceBattle(dancer);
                }
            }

            if (!ActionFighterManager.IsFighterPresent(0))
            {
            }
            else
            {
                Fighter coopDancer = ActionFighterManager.GetFighter(0);

                Matrix4x4 dancerMtx = dancer.HumanMotion.Matrix;

                if (coopDancer.HumanMotion.CurrentAnimation != dancer.HumanMotion.CurrentAnimation)
                    coopDancer.HumanMotion.NextAnimation = dancer.HumanMotion.CurrentAnimation;

                coopDancer.HumanMotion.SetPosition(dancer.Position + dancerMtx.LeftDirection * 1.1f + -dancerMtx.ForwardDirection* 0.75f);
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

                OE.LogInfo("spawn start");
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

        private void NormalUpdate()
        {
            if (m_awaitingSpawn)
            {
                CoopPlayerHandle = new EntityHandle(ActionFighterManager.GetFighter(m_coopPlayerIdx));
                CoopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);
                m_awaitingSpawn = false;
            }
            else if (CoopPlayerHandle.UID == 0 || !CoopPlayerHandle.IsValid() || (m_coopPlayerIdx > -1 && !ActionFighterManager.IsFighterPresent(m_coopPlayerIdx)))
            {
                if (!CanCreate())
                    return;

                if (AutomaticallyCreatePlayer)
                {
                    OE.LogInfo("Automatically creating player2");
                    CreatePlayer2(m_remakePlayer);
                    OE.LogInfo(SequenceManager.MissionID);
                }
            }
            else
            {
            }
        }


        public bool CanCreate()
        {
            return !BlacklistedMissions.Contains((int)SequenceManager.MissionID);
        }

        public static bool IsChase()
        {
            uint mission = SequenceManager.MissionID;
            return mission == 409 || mission == 500;
        }

        public static  bool IsDance()
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
            if(oldMission == 300 && newMission == 400)
            {
                Fighter coopPlayer = ActionFighterManager.GetFighter(m_coopPlayerIdx);

                //Random encounter?
                if(coopPlayer.Pointer != IntPtr.Zero)
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
            while (true)
            {
                if (OE.IsKeyHeld(VirtualKey.LeftShift))
                    if (OE.IsKeyDown(VirtualKey.P))
                        CreatePlayer2(false);

                if (OE.IsKeyHeld(VirtualKey.LeftShift))
                    if (OE.IsKeyDown(VirtualKey.L))
                        ActionFighterManager.DestroyFighter(m_coopPlayerIdx);


                if (OE.IsKeyHeld(VirtualKey.LeftControl))
                    if (OE.IsKeyHeld(VirtualKey.Menu))
                        if (OE.IsKeyDown(VirtualKey.R))
                            IniSettings.Read();

                if (OE.IsKeyHeld(VirtualKey.LeftControl))
                {
                    if (OE.IsKeyDown(VirtualKey.Y))
                    {
                        Thread thread = new Thread(PlayerInput.RemapThread);
                        thread.Start();
                    }
                }
            }
        }


        private void DestroyAndRecreatePlayer2()
        {
            if(m_coopPlayerIdx >= 0 && ActionFighterManager.IsFighterPresent(m_coopPlayerIdx))
            {
                ActionFighterManager.DestroyFighter(m_coopPlayerIdx);
            }

            Reset();
            m_remakePlayer = true;
        }

        private void CreatePlayer2(bool recreate)
        {
            IntPtr inputUpdateAddr = Y5Lib.Unsafe.CPP.PatternSearch("49 63 C7 49 0F 45 D6 48 69 C8 ? ? ? ? 48 03 CB E8 ? ? ? ? 8B 84 24 D0 01 00 00");

            if (inputUpdateAddr != IntPtr.Zero)
                Y5Lib.Unsafe.CPP.PatchMemory(inputUpdateAddr, 0xB8, 0x9, 0x0, 0x0, 0x0, 0x90, 0x90);

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
                    inf.spawnPosition = ActionFighterManager.Player.Position + new Vector3(1, 0, 0);
            }
            else
            {
                inf.spawnPosition = ActionFighterManager.Player.Position + -ActionFighterManager.Player.HumanMotion.Matrix.ForwardDirection * 0.65f;
            }


            inf.N0000054E = p1Dispose.N0000054E;
            inf.battleStartAnim.Set("eMID_NONE");
            inf.N0000453C = -1;

            m_coopPlayerIdx = ActionFighterManager.SpawnCharacter(inf);
            m_awaitingSpawn = true;

            ActionFighterManager.SetPlayer(0);

            OE.LogInfo("spawn");
        }

        private void CreatePlayer2ForDanceBattle(Human haruka)
        {
            IntPtr inputUpdateAddr = Y5Lib.Unsafe.CPP.PatternSearch("49 63 C7 49 0F 45 D6 48 69 C8 ? ? ? ? 48 03 CB E8 ? ? ? ? 8B 84 24 D0 01 00 00");

            if (inputUpdateAddr != IntPtr.Zero)
                Y5Lib.Unsafe.CPP.PatchMemory(inputUpdateAddr, 0xB8, 0x9, 0x0, 0x0, 0x0, 0x90, 0x90);

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

            if(playerModel.Contains("naked"))
                return "c_cm_ichiban_naked";

            if (playerModel.Contains("saejima_g"))
                return "c_cm_ichiban_g";

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
            if(SequenceManager.MissionID == 242)
                return "c_cm_ichiban_haruka_lesson";
           else 
                return "c_cm_ichiban_haruka";
        }
    }
}
