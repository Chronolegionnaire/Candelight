using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Candlelight
{
    public class BlockEntityCandelabra : BlockEntity
    {
        public int CandleCount { get; private set; }
        public bool Lit { get; private set; }

        public string HorFacing = "north";
        public BlockFacing AttachFace = BlockFacing.UP;

        readonly Dictionary<string, Vec3f[]> wickPointCache = new();
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
            tree.SetInt("candles", CandleCount);
            tree.SetBool("lit", Lit);
            tree.SetString("horFacing", HorFacing);
            tree.SetString("attachFace", AttachFace.Code);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);

            CandleCount = tree.GetInt("candles");
            Lit = tree.GetBool("lit");
            HorFacing = tree.GetString("horFacing", "north");

            string faceCode = tree.GetString("attachFace", "up");
            AttachFace = BlockFacing.FromCode(faceCode) ?? BlockFacing.UP;

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

        public Vec3f[] GetOrCreateWickPoints()
        {
            GetOrCreateMesh();

            string baseName = Block.Code.Path.Split('-')[0];

            string orient =
                AttachFace == BlockFacing.UP ? "up" :
                AttachFace == BlockFacing.DOWN ? "down" :
                "wall";

            string glow = Lit ? "-glow" : "";
            var shapeLoc = new AssetLocation(
                "candlelight",
                $"shapes/block/{baseName}/{baseName}-{orient}-candle{CandleCount}{glow}.json"
            );

            string key = $"{shapeLoc}|{AttachFace.Code}|{CandleCount}|{Lit}|{HorFacing}";
            wickPointCache.TryGetValue(key, out var pts);
            return pts;
        }
        static Vec3f RotateZ180AroundCenter(Vec3f p)
        {
            return new Vec3f(1f - p.X, 1f - p.Y, p.Z);
        }

        static Vec3f TranslateY(Vec3f p, float dy)
        {
            return new Vec3f(p.X, p.Y + dy, p.Z);
        }
        float GetYawDeg()
        {
            const float modelOffsetDeg = 90f;

            if (AttachFace == BlockFacing.UP || AttachFace == BlockFacing.DOWN)
            {
                BlockFacing hfacing = BlockFacing.FromCode(HorFacing) ?? BlockFacing.NORTH;
                int idx = hfacing.HorizontalAngleIndex % 4;
                return idx * 90f + modelOffsetDeg;
            }
            else
            {
                int idx = (AttachFace.HorizontalAngleIndex + 2) % 4;
                return idx * 90f + modelOffsetDeg;
            }
        }

        static Vec3f RotatePointLikeMesh(Vec3f p, float yawRad)
        {
            float ox = 0.5f, oz = 0.5f;

            float x = p.X - ox;
            float z = p.Z - oz;

            float cos = GameMath.Cos(yawRad);
            float sin = GameMath.Sin(yawRad);

            float rx = x * cos - z * sin;
            float rz = x * sin + z * cos;

            return new Vec3f(ox + rx, p.Y, oz + rz);
        }

        Vec3f[] ExtractWickPointsFromShape(Shape shape)
        {
            var jointsById = new Dictionary<int, AnimationJoint>();
            var animations = Array.Empty<Animation>();

            var animator = new ClientAnimator(() => 1.0, animations, shape.Elements, jointsById);
            animator.OnFrame(new Dictionary<string, AnimationMetaData>(), 0f);

            var points = new List<Vec3f>();
            for (int i = 1; i <= MaxCandles; i++)
            {
                string code = "Point" + i;
                var apap = animator.GetAttachmentPointPose(code);
                if (apap == null) continue;

                var m = new Matrixf();
                m.Identity();
                apap.MulUncentered(m);

                points.Add(new Vec3f(m.Values[12], m.Values[13], m.Values[14]));
            }

            return points.ToArray();
        }

        MeshData GetOrCreateMesh()
        {
            if (CandleCount < 0) CandleCount = 0;
            if (CandleCount > MaxCandles) CandleCount = MaxCandles;

            string baseName = Block.Code.Path.Split('-')[0];
            string orient =
                AttachFace == BlockFacing.UP ? "up" :
                AttachFace == BlockFacing.DOWN ? "down" :
                "wall";

            string glow = Lit ? "-glow" : "";
            var shapeLoc = new AssetLocation(
                "candlelight",
                $"shapes/block/{baseName}/{baseName}-{orient}-candle{CandleCount}{glow}.json"
            );

            string key = $"{shapeLoc}|{AttachFace.Code}|{CandleCount}|{Lit}|{HorFacing}";
            if (meshCache.TryGetValue(key, out var cachedMesh) && wickPointCache.TryGetValue(key, out var cachedPts))
            {
                return cachedMesh.Clone();
            }

            Shape shape = capi.Assets.TryGet(shapeLoc)?.ToObject<Shape>();
            if (shape == null) return null;

            capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh);
            if (mesh == null) return null;

            float yawDeg = GetYawDeg();
            float yawRad = yawDeg * GameMath.DEG2RAD;

            var pts = ExtractWickPointsFromShape(shape);
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = RotatePointLikeMesh(pts[i], -yawRad);
            }

            wickPointCache[key] = pts;
            mesh.Rotate(Origin, 0, yawRad, 0);

            meshCache[key] = mesh;
            return mesh.Clone();
        }
    }
}
