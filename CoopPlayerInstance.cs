using Y5Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Y5Coop;
using Y5Lib;

namespace Y5Coop
{
    public class CoopPlayerInstance
    {
        public Player Fighter = new Player();
        public int FighterIndex = -1;
        public int PlayerIndex = -1;
        public int InputType = 0;
        public int SpecialUID = 0xBEEF;

        public bool CanHAct = false;

        /// <summary>
        /// We are waiting a set amount of time before spawning them.
        /// </summary>
        public bool IsDelayedSpawn;

        /// <summary>
        /// We told the game to spawn them and are just waiting for them to spawn.
        /// </summary>
        public bool AwaitingSpawn;

        /// <summary>
        /// How much left until we will spawn
        /// </summary>
        public float DelayedSpawnTime = 0;

        private bool m_battleStartAnimDoOnce = false;

        private float m_teleportBlockage = 0;


        private IntPtr m_rawInputData = IntPtr.Zero;

        public void Awake()
        {
            InputType = PlayerIndex + 1;

            //assign a special HAct UID for our lovely player
            switch (PlayerIndex)
            {
                default:
                    SpecialUID = 0xBEEF;
                    break;
                case 0:
                    SpecialUID = 0xBEEF;
                    break;
                case 1:
                    SpecialUID = 0xC0FFEE;
                    break;
                case 2:
                    SpecialUID = 0xB00B1E5;
                    break;
            }
        }

        public void Update()
        {
            if (IsDelayedSpawn)
            {
                if (DelayedSpawnTime <= 0)
                {
                    CreateFighter();
                    DelayedSpawnTime = 0;
                    IsDelayedSpawn = false;
                }
                else
                {
                    DelayedSpawnTime -= ActionManager.DeltaTime;
                }
            }

            if (m_teleportBlockage > 0)
                m_teleportBlockage = m_teleportBlockage - ActionManager.DeltaTime;

            if (IsPresent())
            {
                Vector3 playerPos = ActionFighterManager.GetFighter(0).Position;
                float dist = Vector3.Distance(Fighter.Position, playerPos);
                float heightDiff = Math.Abs(playerPos.y - Fighter.Position.y);

                if (dist >= Mod.TeleportDistance)
                    WarpToMainPlayer();
            }

            if ((GetInputFlags() & 256) != 0 && m_teleportBlockage <= 0)
            {
                WarpToMainPlayer();
                m_teleportBlockage = 0.5f;
            }
        }

        /// <summary>
        /// Seperate from fighter. This is direct keyboard/controller input button data (no analog)
        /// </summary>
        /// <returns></returns>
        public short GetInputFlags()
        {
            if (m_rawInputData == IntPtr.Zero)
                return 0;

            return Marshal.ReadInt16(m_rawInputData);
        }

        public void OnSpawn()
        {
            Fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot((InputDeviceType)InputType));
            m_rawInputData = InputDeviceData.GetRawData((InputDeviceType)InputType);

           Fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot((InputDeviceType)InputType));

#warning TODO: Fix

            if (Mod.IsBattle())
            {
                Fighter.ModeManager.SetCommandset(Mod.CoopPlayerCommandset);
            }
        }

        public void WarpToMainPlayer()
        {
            if (!IsPresent())
                return;

#warning TODO:
            /*
            Fighter player = ActionFighterManager.GetFighterByIndex(0);
            Fighter.WarpToPosition(player.GetPosition() + -(player.Motion.Matrix.ForwardDirection * 1f));
            Fighter.Motion.RotY = player.Motion.RotY;
            */
        }

        public void OnHActStart()
        {

        }

        public void OnHActEnd()
        {

        }

        public void CreateFighter()
        {
            if (IsPresent())
            {
                //DestroyAndRecreateFighter
                ResetInput();
                ActionFighterManager.DestroyFighter(FighterIndex);
                return;
                //throw new NotImplementedException("Unimplemented: DestroyAndRecreateFighter");
            }

            DisposeInfo p1Dispose = ActionFighterManager.Player.Dispose;

            DisposeInfo inf = new DisposeInfo();
            inf.modelName.Set(Mod.GetModelForPlayer2());

            NPCType playerType;

            if (Mod.AllyMode)
                playerType = NPCType.Npc;
            else
                playerType = p1Dispose.FighterType;

            if (Mod.AllyMode)
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

            inf.FighterType = playerType;

            FighterIndex = ActionFighterManager.SpawnCharacter(inf);

            if (FighterIndex >= 0)
                AwaitingSpawn = true;

            inf.battleStartAnim.Set(Mod.ChosenBtlst.ToString());
        }

        public static void PlayBattleStartAnimations()
        {
            /*
            if (!Mod.DoesCoopPlayersExist())
                return;

            foreach (var coopPlayer in Mod.CoopPlayers)
            {
                if (coopPlayer.IsPresent())
                    coopPlayer.Fighter.ModeManager.ToBattleStart();
            }
            */
        }

        /// <summary>
        /// Does Fighter of this player exist
        /// </summary>
        /// <returns></returns>
        public bool IsPresent()
        {
            if (AwaitingSpawn || IsDelayedSpawn)
                return false;

            if (FighterIndex < 0)
                return false;

            if (!ActionFighterManager.IsFighterPresent(FighterIndex))
                return false;

            return true;
        }


        /// <summary>
        /// Resets input slot to 0 (player 1)
        /// </summary>
        /// <returns></returns>
        public void ResetInput()
        {
            if (!IsPresent())
                return;

            Fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(0));
        }

        /// <summary>
        /// The model player shoulkd use when being spawned.
        /// </summary>
        public string TargetModel
        {
            get
            {
                return "c_cm_ichiban";
            }
        }
    }
}
