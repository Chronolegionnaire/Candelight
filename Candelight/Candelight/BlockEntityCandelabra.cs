using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Candlelight
{
    public class BlockEntityCandelabra : BlockEntity
    {
        public int  CandleCount { get; private set; }
        public bool Lit         { get; private set; }

        public string HorFacing = "north";
        public BlockFacing AttachFace = BlockFacing.UP;

        public int MaxCandles => Block?.Attributes?["maxCandles"].AsInt(1) ?? 1;

        ICoreClientAPI capi;
        readonly Dictionary<string, MeshData> meshCache = new();
        static readonly Vec3f Origin = new(0.5f, 0.5f, 0.5f);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            capi = api as ICoreClientAPI;
        }

        public void SetHorizontalFacing(BlockFacing facing)
        {
            HorFacing = facing.Code;
            MarkDirty(true);
        }

        public void SetAttachFace(BlockFacing face)
        {
            AttachFace = face;
            MarkDirty(true);
        }

        public void AddCandle()
        {
            var cblock = Block as BlockCandelabra;
            byte[] oldLight = cblock?.GetLightHsv(Api.World.BlockAccessor, Pos);

            CandleCount++;
            MarkDirty(true);

            cblock?.RequestRelight(Api.World, Pos, oldLight);
        }

        public void RemoveCandle()
        {
            if (CandleCount <= 0) return;

            var cblock = Block as BlockCandelabra;
            byte[] oldLight = cblock?.GetLightHsv(Api.World.BlockAccessor, Pos);

            CandleCount--;
            if (CandleCount == 0) Lit = false;

            MarkDirty(true);

            cblock?.RequestRelight(Api.World, Pos, oldLight);
        }

        public void ToggleLit()
        {
            var cblock = Block as BlockCandelabra;
            byte[] oldLight = cblock?.GetLightHsv(Api.World.BlockAccessor, Pos);

            Lit = !Lit;
            MarkDirty(true);

            cblock?.RequestRelight(Api.World, Pos, oldLight);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt   ("candles",   CandleCount);
            tree.SetBool  ("lit",       Lit);
            tree.SetString("horFacing", HorFacing);
            tree.SetString("attachFace", AttachFace.Code);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);

            CandleCount = tree.GetInt("candles");
            Lit         = tree.GetBool("lit");
            HorFacing   = tree.GetString("horFacing", "north");

            string faceCode = tree.GetString("attachFace", "up");
            AttachFace      = BlockFacing.FromCode(faceCode) ?? BlockFacing.UP;

            if (world.Side == EnumAppSide.Server)
            {
                (Block as BlockCandelabra)?.RequestRelight(world, Pos);
            }
            else
            {
                MarkDirty(true);
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess)
        {
            if (capi == null || Block == null) return false;

            MeshData mesh = GetOrCreateMesh();
            if (mesh == null) return false;

            mesher.AddMeshData(mesh);
            return true;
        }

        MeshData GetOrCreateMesh()
        {
            if (CandleCount < 0) CandleCount = 0;
            if (CandleCount > MaxCandles) CandleCount = MaxCandles;

            string baseName = Block.Code.Path.Split('-')[0];

            string orient =
                AttachFace == BlockFacing.UP   ? "up" :
                AttachFace == BlockFacing.DOWN ? "down" :
                                                 "wall";

            string glow = Lit ? "-glow" : "";

            var shapeLoc = new AssetLocation(
                "candlelight",
                $"shapes/block/{baseName}/{baseName}-{orient}-candle{CandleCount}{glow}.json"
            );

            string key = $"{shapeLoc}|{AttachFace.Code}|{CandleCount}|{Lit}|{HorFacing}";
            if (meshCache.TryGetValue(key, out var cached)) return cached.Clone();

            Shape shape = capi.Assets.TryGet(shapeLoc)?.ToObject<Shape>();
            if (shape == null) return null;

            capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh);
            if (mesh == null) return null;

            ApplyRotation(mesh);

            meshCache[key] = mesh;
            return mesh.Clone();
        }
        void ApplyRotation(MeshData mesh)
        {
            if (AttachFace == BlockFacing.UP || AttachFace == BlockFacing.DOWN)
            {
                BlockFacing hfacing = BlockFacing.FromCode(HorFacing) ?? BlockFacing.NORTH;
                int idx = hfacing.HorizontalAngleIndex;
                float yawDeg = ((idx + 1) % 4) * 90f;

                mesh.Rotate(Origin, 0, GameMath.DEG2RAD * yawDeg, 0);
            }
            else
            {
                BlockFacing facing = AttachFace;

                int idx = facing.HorizontalAngleIndex;
                float yawDeg = ((idx + 1) % 4) * 90f + 180f;

                mesh.Rotate(Origin, 0, GameMath.DEG2RAD * yawDeg, 0);
            }
        }
    }
}
