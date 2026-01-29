using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Candlelight
{
    public class BlockCandelabra : Block
    {
        ICoreClientAPI capi;
        AdvancedParticleProperties[] litParticles;
        static void PlayAddRemoveCandleSound(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            world.PlaySoundAt(
                new AssetLocation("game", "sounds/block/planks"),
                blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z,
                byPlayer, true, 32f, 1f
            );
        }

        static void PlayLightCandleSound(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            world.PlaySoundAt(
                new AssetLocation("game", "sounds/effect/extinguish1"),
                blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z,
                byPlayer, true, 32f, 1f
            );
        }

        static void PlayUnlightCandleSound(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            world.PlaySoundAt(
                new AssetLocation("game", "sounds/effect/extinguish2"),
                blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z,
                byPlayer, true, 32f, 1f
            );
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            capi = api as ICoreClientAPI;

            litParticles = Attributes?["litParticles"]?.AsObject<AdvancedParticleProperties[]>();

            if (api.Side == EnumAppSide.Client && litParticles != null)
            {
                for (int i = 0; i < litParticles.Length; i++)
                {
                    litParticles[i].Init(api);
                }
            }
        }
        
        public void RequestRelight(IWorldAccessor world, BlockPos pos, byte[] oldLightHsV = null)
        {
            if (world.Side != EnumAppSide.Server) return;
            if (oldLightHsV != null)
            {
                world.BlockAccessor.RemoveBlockLight(oldLightHsV, pos);
            }
            world.RegisterCallback(_ =>
            {
                var relightAcc = world.GetBlockAccessor(synchronize: true, relight: true, strict: false);

                int blockId = relightAcc.GetBlockId(pos);
                relightAcc.ExchangeBlock(blockId, pos);
                relightAcc.MarkBlockDirty(pos);
                relightAcc.MarkBlockEntityDirty(pos);

            }, 1);
        }


        public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos,
            float windAffectednessAtPos, float secondsTicking)
        {
            if (litParticles == null || litParticles.Length == 0) return;

            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityCandelabra;
            if (be == null || !be.Lit || be.CandleCount <= 0) return;
            Vec3f[] wickPoints = be.GetOrCreateWickPoints();
            if (wickPoints == null || wickPoints.Length == 0) return;

            int countToSpawn = GameMath.Clamp(be.CandleCount, 1, wickPoints.Length);

            for (int p = 0; p < countToSpawn; p++)
            {
                Vec3f local = wickPoints[p];

                for (int i = 0; i < litParticles.Length; i++)
                {
                    var bps = litParticles[i];
                    bps.WindAffectednesAtPos = windAffectednessAtPos;

                    bps.basePos.X = pos.X + local.X;
                    bps.basePos.Y = pos.InternalY + local.Y;
                    bps.basePos.Z = pos.Z + local.Z;

                    manager.Spawn(bps);
                }
            }
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
            BlockSelection blockSel, ref string failureCode)
        {
            var pos      = blockSel.Position;
            var here     = world.BlockAccessor.GetBlock(pos);
            var attachTo = blockSel.Face;
            var supportPos = pos.AddCopy(attachTo.Opposite);
            var support    = world.BlockAccessor.GetBlock(supportPos);
            if (here.Replaceable < this.Replaceable)
            {
                failureCode = "notreplaceable";
                return false;
            }
            if (!support.CanAttachBlockAt(world.BlockAccessor, this, supportPos, attachTo))
            {
                failureCode = "requireattachable";
                return false;
            }

            if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode))
            {
                return false;
            }

            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCandelabra;
            if (be != null)
            {
                be.SetAttachFace(attachTo);
                if (attachTo == BlockFacing.UP || attachTo == BlockFacing.DOWN)
                {
                    float yaw = (float)(byPlayer?.Entity?.SidedPos?.Yaw ?? 0f);
                    BlockFacing hfacing = BlockFacing.HorizontalFromYaw(yaw);
                    be.SetHorizontalFacing(hfacing);
                }

                be.MarkDirty(true);
            }

            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCandelabra;
            if (be == null) return;

            BlockFacing attachFace = be.AttachFace;
            BlockPos supportPos    = pos.AddCopy(attachFace.Opposite);
            Block support          = world.BlockAccessor.GetBlock(supportPos);

            if (!support.CanAttachBlockAt(world.BlockAccessor, this, supportPos, attachFace))
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityCandelabra;
            if (be == null) return false;

            bool sneak    = byPlayer.Entity.Controls.ShiftKey;
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStack held = slot?.Itemstack;

            bool isCandle = held?.Collectible.Code.Path == "candle";
            bool survival = byPlayer.WorldData.CurrentGameMode == EnumGameMode.Survival;

            if (sneak)
            {
                if (be.CandleCount <= 0)
                {
                    capi?.TriggerIngameError(this, "candelabraempty", Lang.Get("candelabraempty"));
                    return false;
                }

                if (survival)
                {
                    var candle = new ItemStack(world.GetItem(new AssetLocation("candle")), 1);
                    if (!byPlayer.InventoryManager.TryGiveItemstack(candle)) return false;
                }
                be.RemoveCandle();
                PlayAddRemoveCandleSound(world, byPlayer, blockSel);
                return true;
            }
            if (isCandle)
            {
                if (be.CandleCount >= be.MaxCandles)
                {
                    capi?.TriggerIngameError(this, "candelabrafull", "Candelabra is full");
                    return false;
                }

                if (survival)
                {
                    slot.TakeOut(1);
                    slot.MarkDirty();
                }

                be.AddCandle();
                PlayAddRemoveCandleSound(world, byPlayer, blockSel);
                return true;
            }
            if (be.CandleCount == 0)
            {
                capi?.TriggerIngameError(this, "notenoughcandles", Lang.Get("needcandlestolight"));
                return false;
            }
            bool willLight = !be.Lit;

            be.ToggleLit();

            if (willLight) PlayLightCandleSound(world, byPlayer, blockSel);
            else           PlayUnlightCandleSound(world, byPlayer, blockSel);

            return true;
        }

        public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
        {
            if (pos == null) return base.GetLightHsv(blockAccessor, pos, stack);

            var be = blockAccessor.GetBlockEntity(pos) as BlockEntityCandelabra;
            if (be == null || !be.Lit || be.CandleCount <= 0)
            {
                return base.GetLightHsv(blockAccessor, pos, stack);
            }

            byte h = 7;
            byte s = 7;
            byte v = (byte)(6 + be.CandleCount);

            return new byte[] { h, s, v };
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            byte[] oldLight = GetLightHsv(world.BlockAccessor, pos);
            world.BlockAccessor.RemoveBlockLight(oldLight, pos);

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetOrientedBoxes(blockAccessor, pos, forCollision: false);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetOrientedBoxes(blockAccessor, pos, forCollision: true);
        }

        private Cuboidf[] GetOrientedBoxes(IBlockAccessor ba, BlockPos pos, bool forCollision)
        {
            var be = ba.GetBlockEntity(pos) as BlockEntityCandelabra;
            if (be == null)
            {
                return forCollision ? base.GetCollisionBoxes(ba, pos) : base.GetSelectionBoxes(ba, pos);
            }
            Pose pose = PoseFromAttachFace(be.AttachFace);
            int variant = GetVariantNumberFromCodePath(Code?.Path);
            Cuboidf origin = GetOriginBox(variant, pose);
            int rotSteps = GetYawSteps(be);
            Cuboidf rotated = RotateY90Steps(origin.Clone(), rotSteps);
            if (pose == Pose.Wall && (be.AttachFace == BlockFacing.EAST || be.AttachFace == BlockFacing.WEST))
            {
                rotated = RotateZ180(rotated);
                rotated = TranslateY(rotated, -0.25f);
            }

            return new[] { rotated };
        }
        private static Cuboidf TranslateY(Cuboidf b, float dy)
        {
            return new Cuboidf(
                b.X1,
                b.Y1 + dy,
                b.Z1,
                b.X2,
                b.Y2 + dy,
                b.Z2
            );
        }

        private enum Pose { Up, Down, Wall }

        private static Pose PoseFromAttachFace(BlockFacing face)
        {
            if (face == BlockFacing.UP) return Pose.Up;
            if (face == BlockFacing.DOWN) return Pose.Down;
            return Pose.Wall;
        }

        private static int GetVariantNumberFromCodePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1;

            const string prefix = "candelabra";

            if (!path.StartsWith(prefix)) return 1;

            int idx = prefix.Length;

            if (idx < path.Length && char.IsDigit(path[idx]))
            {
                int n = path[idx] - '0';
                return GameMath.Clamp(n, 1, 3);
            }

            return 1;
        }

        private Cuboidf GetOriginBox(int variant, Pose pose)
        {
            variant = GameMath.Clamp(variant, 1, 3);

            switch (variant)
            {
                case 1:
                    switch (pose)
                    {
                        case Pose.Up:
                            return new Cuboidf(0.375f, 0f, 0.375f, 0.625f, 0.687f, 0.625f);

                        case Pose.Down:
                            return new Cuboidf(0.375f, 0.2f, 0.375f, 0.625f, 1f, 0.625f);

                        case Pose.Wall:
                        default:
                            return new Cuboidf(0f, 0.05f, 0.375f, 0.375f, 0.687f, 0.625f);
                    }

                case 2:
                    switch (pose)
                    {
                        case Pose.Up:
                            return new Cuboidf(0.375f, 0f, 0.293f, 0.632f, 0.687f, 0.707f);

                        case Pose.Down:
                            return new Cuboidf(0.375f, 0.2f, 0.293f, 0.632f, 1f, 0.707f);

                        case Pose.Wall:
                        default:
                            return new Cuboidf(0f, 0.05f, 0.293f, 0.375f, 0.687f, 0.707f);
                    }

                case 3:
                default:
                    switch (pose)
                    {
                        case Pose.Up:
                            return new Cuboidf(0.375f, 0f, 0.255f, 0.632f, 0.718f, 0.745f);

                        case Pose.Down:
                            return new Cuboidf(0.375f, 0.20f, 0.255f, 0.632f, 1f, 0.745f);

                        case Pose.Wall:
                        default:
                            return new Cuboidf(0f, 0.05f, 0.255f, 0.375f, 0.718f, 0.745f);
                    }
            }
        }

        private int GetYawSteps(BlockEntityCandelabra be)
        {
            if (be.AttachFace == BlockFacing.UP || be.AttachFace == BlockFacing.DOWN)
            {
                var hf = BlockFacing.FromCode(be.HorFacing) ?? BlockFacing.NORTH;
                return hf.HorizontalAngleIndex % 4;
            }
            else
            {
                var f = be.AttachFace;
                return (f.HorizontalAngleIndex + 2) % 4;
            }
        }
        private static Cuboidf RotateZ180(Cuboidf b)
        {
            float nx1 = 1f - b.X2;
            float nx2 = 1f - b.X1;

            float ny1 = 1f - b.Y2;
            float ny2 = 1f - b.Y1;

            float rx1 = GameMath.Min(nx1, nx2), rx2 = GameMath.Max(nx1, nx2);
            float ry1 = GameMath.Min(ny1, ny2), ry2 = GameMath.Max(ny1, ny2);

            return new Cuboidf(rx1, ry1, b.Z1, rx2, ry2, b.Z2);
        }
        private static Cuboidf RotateY90Steps(Cuboidf b, int steps)
        {
            steps = ((steps % 4) + 4) % 4;

            float x1 = b.X1, x2 = b.X2, z1 = b.Z1, z2 = b.Z2;

            for (int i = 0; i < steps; i++)
            {
                float nx1 = 1f - z2;
                float nx2 = 1f - z1;
                float nz1 = x1;
                float nz2 = x2;

                x1 = nx1;
                x2 = nx2;
                z1 = nz1;
                z2 = nz2;
            }
            float rx1 = GameMath.Min(x1, x2), rx2 = GameMath.Max(x1, x2);
            float rz1 = GameMath.Min(z1, z2), rz2 = GameMath.Max(z1, z2);

            return new Cuboidf(rx1, b.Y1, rz1, rx2, b.Y2, rz2);
        }
    }
}
