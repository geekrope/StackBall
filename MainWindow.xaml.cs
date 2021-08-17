using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Threading;

namespace StackBall
{
    public enum BallState
    {
        Jumping, Hitting
    }

    public static class Extentions
    {
        public static Model3DCollection ExpandCollection(this List<Model3DCollection> collections)
        {
            var result = new Model3DCollection();
            foreach (var item in collections)
            {
                foreach (var model in item)
                {
                    result.Add(model);
                }
            }
            return result;
        }
        public static Transform3D AddTransform(this Transform3D origin, Transform3D addition)
        {
            return new Transform3DGroup() { Children = new Transform3DCollection() { origin, addition } };
        }
        public static Model3DCollection Merge(this MeshGeometry3D[] geometryes3D, Material material)
        {
            var collection = new Model3DCollection();
            foreach (var geometry in geometryes3D)
            {
                var model3D = new GeometryModel3D(geometry, material);
                model3D.SetValue(GeometryModel3D.BackMaterialProperty, material);
                collection.Add(model3D);
            }
            return collection;
        }
        public static double NormalizeAngle(double angle)
        {
            angle %= 360;
            if (angle < 0)
            {
                angle += 360;
            }
            return angle;
        }
        public static double DegreesToRadians(double angle)
        {
            return angle / 180 * Math.PI;
        }
        public static double RadiansToDegrees(double angle)
        {
            return angle / Math.PI * 180;
        }
    }

    public class PlayerBall
    {
        private Point3D OriginalPosition
        {
            get; set;
        }
        private int JumpFrame;
        private int Tick = 0;
        private const int JumpHeight = 1;
        private const int JumpFrames = 40;
        /// <summary>
        /// Radius count
        /// </summary>
        public static readonly (double, int) BallSettings = (0.2, 20);
        public BallState State
        {
            get; set;
        }
        public Model3DCollection Ball
        {
            get; private set;
        }
        public double OffsetY
        {
            get; set;
        }
        public Block CurrentBlock
        {
            get; set;
        }
        public Action OnHit
        {
            get; set;
        }
        public Action OnDie
        {
            get; set;
        }
        public const int TimeOffset = 3;
        public int HittedCount
        {
            get; private set;
        }
        public int Power
        {
            get; private set;
        }
        public bool Invulnerable
        {
            get; private set;
        }
        public const int MaxPower = 40;
        public const int PowerSpeed = 10;

        private MeshGeometry3D[] GetSphere(double radius, int count)
        {
            var parts = new MeshGeometry3D[2];

            Func<int, MeshGeometry3D> getHalfSphere = (int sign) =>
            {
                var vertices = new List<Point3D>();
                var indices = new List<int>();

                Action<int> createVertices = (int sign) =>
                {
                    for (int indexY = 0; indexY < count; indexY += 1)
                    {
                        var angleY = Math.PI / (count - 1) * indexY - Math.PI / 2;
                        var y = Math.Sin(angleY) * radius;
                        var parallelRadius = Math.Abs(Math.Sqrt(Math.Pow(radius, 2) - Math.Pow(y, 2)));

                        for (int indexX = 0; indexX < count; indexX += 1)
                        {
                            var angleX = Math.PI / (count - 1) * indexX;
                            var x = Math.Cos(angleX) * parallelRadius;
                            var z = Math.Sin(angleX) * parallelRadius * sign;

                            vertices.Add(new Point3D(x, y, z));
                        }
                    }
                };

                Action<int> getIndices = (int offset) =>
                {
                    for (int index = 0; index < count * (count - 1); index += 1)
                    {
                        var index1 = index + offset;
                        var index2 = index + 1 + offset;
                        var index3 = index + count + offset;
                        var index4 = index + count + 1 + offset;
                        indices.AddRange(new int[] { index3, index1, index2, index3, index2, index4 });
                    }
                };

                Func<List<Point3D>, List<int>, MeshGeometry3D> create3DObject = (List<Point3D> vertices, List<int> indices) =>
                {
                    return new MeshGeometry3D() { Positions = new Point3DCollection(vertices), TriangleIndices = new Int32Collection(indices) };
                };

                createVertices(sign);
                getIndices(0);

                return create3DObject(vertices, indices);
            };


            parts[0] = getHalfSphere(1);
            parts[1] = getHalfSphere(-1);

            return parts;
        }
        private void SetTransform()
        {
            var x = (double)JumpFrame - JumpFrames / 2;
            var jumpOffset = (-Math.Pow(x / JumpFrames * 2, 2) + 1) * JumpHeight;

            var delta = 0.3;
            var scale = 1 - (double)(JumpFrame - JumpFrames / 2) / JumpFrames * 2 * delta;
            if (State == BallState.Hitting)
            {
                scale = 1;
            }

            foreach (var obj in Ball)
            {
                obj.Transform = new Transform3DGroup()
                {
                    Children = new Transform3DCollection(new Transform3D[] { new TranslateTransform3D(OriginalPosition.X, jumpOffset + OriginalPosition.Y + OffsetY, OriginalPosition.Z),
                        new ScaleTransform3D(1, scale, 1)
                    })
                };
            }
        }
        private bool AngleInArc(double startAngle, double sweepAngle, double angle)
        {
            if (startAngle + sweepAngle > 360) //Arc goes through 0 deg
            {
                var arc1 = (startAngle, 360 - startAngle);
                var arc2 = (0, Extentions.NormalizeAngle(startAngle + sweepAngle));
                return AngleInArc(arc1.Item1, arc1.Item2, angle) || AngleInArc(arc2.Item1, arc2.Item2, angle);
            }
            else
            {
                return angle > startAngle && angle - startAngle < sweepAngle;
            }
        }

        public bool CanHit()
        {
            var z = Block.BlockSettings.Item2 - Block.BlockSettings.Item3 / 2;
            var x1 = BallSettings.Item1 / 2;
            var x2 = -BallSettings.Item1 / 2;
            var angularDistance = (Extentions.NormalizeAngle(Extentions.RadiansToDegrees(Math.Atan2(z, x1))),
                Extentions.NormalizeAngle(Extentions.RadiansToDegrees(Math.Atan2(z, x2))));
            var canHit = true;
            foreach (var deadZone in CurrentBlock.DeadZones)
            {
                var angle = Extentions.NormalizeAngle(deadZone.Item1 + CurrentBlock.Angle);

                if (AngleInArc(angle, deadZone.Item2, angularDistance.Item1) || AngleInArc(angle, deadZone.Item2, angularDistance.Item2))
                {
                    canHit = false;
                }
            }
            return canHit;
        }
        public PlayerBall(Point3D position, Block currentBlock)
        {
            Ball = GetSphere(BallSettings.Item1, BallSettings.Item2).Merge(new DiffuseMaterial(Brushes.DodgerBlue));
            State = BallState.Jumping;
            OriginalPosition = position;
            CurrentBlock = currentBlock;
        }
        public void Hit()
        {
            State = BallState.Hitting;
        }
        public void Jump()
        {
            State = BallState.Jumping;
            HittedCount = 0;
        }
        public void Update()
        {
            if (State == BallState.Jumping || (State == BallState.Hitting && JumpFrame != 0))
            {
                SetTransform();
                JumpFrame++;
                JumpFrame %= JumpFrames;
            }
            else if (State == BallState.Hitting)
            {
                SetTransform();
                if (Tick % TimeOffset == 0)
                {
                    if (CanHit() || Invulnerable)
                    {
                        OnHit?.Invoke();
                        if (Power < MaxPower && !Invulnerable)
                        {
                            Power++;
                        }
                        else
                        {
                            Invulnerable = true;
                        }
                        HittedCount++;
                    }
                    else if (!Invulnerable)
                    {
                        OnDie?.Invoke();
                        HittedCount = 0;
                    }
                }
            }

            if (Tick % PowerSpeed == 0 && Power != 0 && (State != BallState.Hitting || Invulnerable))
            {
                Power--;
                if (Power == 0)
                {
                    Invulnerable = false;
                }
            }

            Tick++;
        }
    }

    public class Block
    {
        private int ScaleFrame;
        private const int ScaleFrames = 20;
        private const double ScaleDelta = 0.1;
        private bool Disposing;
        private double DisposeRadius = 0;
        private const double DisposeRadiusDelta = 0.5;
        private const double AngleDelta = 1;
        private (int, int)[] deadZones;
        private List<Model3DCollection> DeadZonesGeometry
        {
            get; set;
        }
        private List<Model3DCollection> HealthZonesGeometry
        {
            get; set;
        }
        private ModelVisual3D Zones
        {
            get; set;
        }
        private void Sort(ref (int, int)[] zones)
        {
            for (int index = 0; index < zones.Length; index++)
            {
                for (int swapIndex = 0; swapIndex < zones.Length - 1; swapIndex++)
                {
                    if (zones[index].Item1 < zones[swapIndex].Item1)
                    {
                        var obj = (zones[index].Item1, zones[index].Item2);
                        zones[index] = zones[swapIndex];
                        zones[swapIndex] = obj;
                    }
                }
            }
        }
        private void CreateGraphics()
        {
            DeadZonesGeometry = new List<Model3DCollection>();
            HealthZonesGeometry = new List<Model3DCollection>();

            Func<double, double, Brush, Model3DCollection> createZone = (double startAngle, double sweepAngle, Brush color) =>
               {
                   var arc = GetArc(BlockSettings.Item1, BlockSettings.Item2, Extentions.DegreesToRadians(startAngle), Extentions.DegreesToRadians(sweepAngle), BlockSettings.Item3, BlockSettings.Item4);
                   return arc.Merge(new DiffuseMaterial(color));
               };

            foreach (var healthZone in HealthZones)
            {
                HealthZonesGeometry.Add(createZone(healthZone.Item1, healthZone.Item2, HealthZoneColor));
            }

            foreach (var deadZone in DeadZones)
            {
                DeadZonesGeometry.Add(createZone(deadZone.Item1, deadZone.Item2, DeadZoneColor));
            }

            var modelsCollection = new Model3DCollection(DeadZonesGeometry.ExpandCollection().Concat(HealthZonesGeometry.ExpandCollection()));
            Zones = new ModelVisual3D() { Content = new Model3DGroup() { Children = modelsCollection } };
        }
        /// <summary>
        /// Gets point of the given arc
        /// </summary>
        /// <param name="height">Arc height in y dimension</param>
        /// <param name="radius">Arc radius</param>
        /// <param name="startAngle">Start angle in radians</param>
        /// <param name="sweepAngle">Sweep angle in radians</param>
        /// <param name="count">Arc points count</param>
        /// <param name="width">Radius inset</param>
        /// <returns>4 Sides of arc</returns>
        private MeshGeometry3D[] GetArc(double height, double radius, double startAngle, double sweepAngle, double width, int count)
        {
            var parts = new MeshGeometry3D[4];

            var verticesTop = new Point3D[count];
            var indicesTop = new List<int>();

            var verticesBottom = new Point3D[count];
            var indicesBottom = new List<int>();

            var verticesBack = new Point3D[count * 2];
            var indicesBack = new List<int>();

            var verticesFront = new Point3D[count * 2];
            var indicesFront = new List<int>();

            Action createVertices = () =>
            {
                for (int index = 0; index < count; index += 2)
                {
                    var angle = startAngle + sweepAngle * index / (count - 2); //counts - 2 equals to max index value
                    var x = Math.Cos(angle) * radius;
                    var z = Math.Sin(angle) * radius;
                    var x2 = Math.Cos(angle) * (radius - width);
                    var z2 = Math.Sin(angle) * (radius - width);

                    verticesTop[index] = new Point3D(x, height / 2, z);
                    verticesTop[index + 1] = new Point3D(x2, height / 2, z2);

                    verticesBottom[index] = new Point3D(x, -height / 2, z);
                    verticesBottom[index + 1] = new Point3D(x2, -height / 2, z2);

                    verticesFront[index] = new Point3D(x, -height / 2, z);
                    verticesFront[index + 1] = new Point3D(x, height / 2, z);

                    verticesBack[index] = new Point3D(x2, -height / 2, z2);
                    verticesBack[index + 1] = new Point3D(x2, height / 2, z2);
                }
            };

            Action getIndices = () =>
            {
                for (int index = 0; index < count - 3; index += 2)
                {
                    var index1 = index;
                    var index2 = index + 1;
                    var index3 = index + 2;
                    var index4 = index + 3;
                    indicesTop.AddRange(new int[] { index3, index1, index2, index3, index2, index4 });
                    indicesBottom.AddRange(new int[] { index3, index1, index2, index3, index2, index4 });
                    indicesFront.AddRange(new int[] { index3, index1, index2, index3, index2, index4 });
                    indicesBack.AddRange(new int[] { index3, index1, index2, index3, index2, index4 });
                }
            };

            Func<Point3D[], List<int>, MeshGeometry3D> create3DObject = (Point3D[] vertices, List<int> indices) =>
            {
                return new MeshGeometry3D() { Positions = new Point3DCollection(vertices), TriangleIndices = new Int32Collection(indices) };
            };

            createVertices();
            getIndices();

            parts[0] = create3DObject(verticesTop, indicesTop);
            parts[1] = create3DObject(verticesBottom, indicesBottom);
            parts[2] = create3DObject(verticesFront, indicesFront);
            parts[3] = create3DObject(verticesBack, indicesBack);

            return parts;
        }
        private void Rotate(List<Model3DCollection> models3D, double angle)
        {
            foreach (var model in models3D)
            {
                foreach (var obj in model)
                {
                    obj.Transform = new Transform3DGroup() { Children = new Transform3DCollection(new Transform3D[] { GetRotationMatrix(), new TranslateTransform3D(0, OffsetY, 0), GetScaleMatrix() }) };
                }
            }
        }
        private void Translate(Model3DCollection collection, Vector3D offset)
        {
            foreach (var element in collection)
            {
                element.Transform = new Transform3DGroup() { Children = new Transform3DCollection(new Transform3D[] { GetRotationMatrix(), new TranslateTransform3D(offset.X, offset.Y + OffsetY + 0.5, offset.Z) }) };
            }
        }
        private RotateTransform3D GetRotationMatrix()
        {
            return new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -Angle));
        }
        private ScaleTransform3D GetScaleMatrix()
        {
            var scale = 1.0;
            if (Scaling)
            {
                scale = (1 + ScaleDelta) - Math.Abs((double)ScaleFrame / ScaleFrames * 2 - 1) * ScaleDelta;
            }
            return new ScaleTransform3D(scale, scale, scale);
        }

        public static readonly SolidColorBrush HealthZoneColor = Brushes.LawnGreen;
        public static readonly SolidColorBrush DeadZoneColor = Brushes.DarkRed;
        /// <summary>
        /// Height, radius, width, count
        /// </summary>
        public static readonly (double, double, double, int) BlockSettings = (0.2, 2, 0.5, 100);
        public bool Scaling
        {
            get; private set;
        }
        public Action<Block> Disposed
        {
            get; set;
        }

        public (int, int)[] DeadZones
        {
            get
            {
                return deadZones;
            }
            set
            {
                deadZones = value;
                Sort(ref deadZones);

                var healthZones = new List<(int, int)>();

                var currentAngle = 0;
                for (int index = 0; index < deadZones.Length; index++)
                {
                    if (currentAngle != deadZones[index].Item1)
                    {
                        var angle = currentAngle;
                        var angle2 = deadZones[index].Item1;
                        healthZones.Add((angle, angle2 - angle));
                    }
                    currentAngle = deadZones[index].Item1 + deadZones[index].Item2;

                    var start = deadZones[deadZones.Length - 1].Item1 + deadZones[deadZones.Length - 1].Item2;
                    if (index == deadZones.Length - 1 && start != 360)
                    {
                        healthZones.Add((start, 360 - start));
                    }
                }

                HealthZones = healthZones.ToArray();

                CreateGraphics();
            }
        }
        public (int, int)[] HealthZones
        {
            get; private set;
        }
        public double OffsetY
        {
            get; set;
        }
        public double Angle
        {
            get; private set;
        }

        public ModelVisual3D GetZones()
        {
            return Zones;
        }
        public void Update()
        {
            if (Scaling)
            {
                if (ScaleFrame == ScaleFrames)
                {
                    Scaling = false;
                    ScaleFrame = 0;
                }
                else
                {
                    ScaleFrame++;
                }
            }
            if (!Disposing)
            {
                Rotate(DeadZonesGeometry, Angle);
                Rotate(HealthZonesGeometry, Angle);
            }
            else
            {
                for (int index = 0; index < HealthZones.Length; index++)
                {
                    var zone = HealthZones[index];
                    var angle = Extentions.DegreesToRadians(Angle + zone.Item1 + zone.Item2 / 2);
                    var disposeDirection = new Vector3D(Math.Cos(angle) * DisposeRadius, 0 * DisposeRadius, Math.Sin(angle) * DisposeRadius);
                    Translate(HealthZonesGeometry[index], disposeDirection);
                }
                for (int index = 0; index < DeadZones.Length; index++)
                {
                    var zone = DeadZones[index];
                    var angle = Extentions.DegreesToRadians(Angle + zone.Item1 + zone.Item2 / 2);
                    var disposeDirection = new Vector3D(Math.Cos(angle) * DisposeRadius, 0 * DisposeRadius, Math.Sin(angle) * DisposeRadius);
                    Translate(DeadZonesGeometry[index], disposeDirection);
                }
                DisposeRadius += DisposeRadiusDelta;
                if (DisposeRadius > MainWindow.ZDepth)
                {
                    Disposed?.Invoke(this);
                }
            }
        }
        public void Dispose()
        {
            Disposing = true;
        }
        public void IncreaseAngle(double delta = AngleDelta)
        {
            if (!Disposing)
            {
                Angle += delta;
                Angle = Angle % 360;
            }
        }
        public void Scale()
        {
            Scaling = true;
        }

        public Block(double offset)
        {
            OffsetY = offset;
        }
    }
    public partial class MainWindow : Window
    {
        public const double ZDepth = 10;
        const double BlockSpot = 0.3;
        const int BlocksBunch = 8;
        int CurrentBlockIndex = 0;
        DispatcherTimer Timer;
        List<Block> Blocks;
        List<Block> VisualizedBlocks;
        List<Block> BlocksToRemove;
        PlayerBall Player;
        Block CurrentBlock
        {
            get
            {
                return Blocks[CurrentBlockIndex];
            }
            set
            {

            }
        }
        bool Hitting = false;

        private void CreateBlocks(int count)
        {
            Blocks = new List<Block>();

            var remainCount = count;
            var random = new Random();
            for (; remainCount > 0;)
            {
                var bunch = Math.Min(remainCount, random.Next(1, 30));
                var blocksCount = random.Next(2, 4);
                var deadZone = (random.Next(0, 20), random.Next(40, 110));
                var deadZone2 = (random.Next(150, 170), random.Next(40, 50));
                var deadZone3 = (random.Next(250, 300), random.Next(20, 30));

                for (int index = 0; index < bunch; index++)
                {
                    var block = new Block(Blocks.Count * -BlockSpot);
                    if (blocksCount == 2)
                    {
                        block.DeadZones = new (int, int)[] { deadZone, deadZone2 };
                    }
                    else if (blocksCount == 3)
                    {
                        block.DeadZones = new (int, int)[] { deadZone, deadZone2, deadZone3 };
                    }

                    block.Disposed = RemoveBlock;
                    block.IncreaseAngle(PlayerBall.TimeOffset * index);

                    Blocks.Add(block);
                }

                remainCount -= bunch;
            }
        }

        private void HitBlock()
        {
            CurrentBlock.Dispose();
            CurrentBlock.Update();

            if (CurrentBlockIndex + 1 < Blocks.Count)
            {
                CurrentBlockIndex++;

                foreach (var block in Blocks)
                {
                    block.OffsetY += BlockSpot;
                }

                if (CurrentBlockIndex + BlocksBunch < Blocks.Count)
                {
                    VisualizedBlocks.Add(Blocks[CurrentBlockIndex + BlocksBunch - 1]);
                    viewport.Children.Add(Blocks[CurrentBlockIndex + BlocksBunch - 1].GetZones());
                }
            }

            Player.CurrentBlock = CurrentBlock;
        }

        private void RemoveBlock(Block block)
        {
            BlocksToRemove.Add(block);
        }

        private void OnDie()
        {
            Timer.Stop();
        }

        private void SetBallState()
        {
            if (Hitting)
            {
                Player.Hit();
            }
            else
            {
                Player.Jump();
            }
        }

        private void AnimateBlocks()
        {
            if (Player.State != BallState.Hitting)
            {
                foreach (var block in Blocks)
                {
                    block.IncreaseAngle();
                }
            }
            else
            {
                foreach (var block in Blocks)
                {
                    block.IncreaseAngle(0.2);
                }
            }

            foreach (var block in VisualizedBlocks)
            {
                block.Update();
            }
        }

        private void RemoveBlocks()
        {
            for (; BlocksToRemove.Count != 0;)
            {
                var block = BlocksToRemove[0];
                VisualizedBlocks.Remove(block);
                BlocksToRemove.Remove(block);
                viewport.Children.Remove(block.GetZones());
            }
        }

        private void AnimatePower()
        {
            if (Player.Power == 0)
            {
                powerBar.Visibility = Visibility.Hidden;
            }
            else
            {
                powerBar.Visibility = Visibility.Visible;
                var arcLength = (double)Player.Power / PlayerBall.MaxPower * Math.PI * 2;
                var radius = power.Width / 2;
                var point1 = new Point(radius * 2, radius);
                var point2 = new Point(radius, radius);
                var point3 = new Point(Math.Cos(arcLength) * radius + radius, -Math.Sin(arcLength) * radius + radius);
                PolyLineSegment clipPolygon = new PolyLineSegment();

                switch (arcLength)
                {
                    case < Math.PI / 2:
                        clipPolygon.Points = new PointCollection(new Point[] { point2, point3, new Point(radius * 2, 0) });
                        break;
                    case < Math.PI:
                        clipPolygon.Points = new PointCollection(new Point[] { point2, point3, new Point(0, 0), new Point(radius * 2, 0) });
                        break;
                    case < Math.PI * 3 / 2:
                        clipPolygon.Points = new PointCollection(new Point[] { point2, point3, new Point(0, radius * 2), new Point(0, 0), new Point(radius * 2, 0) });
                        break;
                    default:
                        clipPolygon.Points = new PointCollection(new Point[] { point2, point3, new Point(radius * 2, radius * 2), new Point(0, radius * 2), new Point(0, 0), new Point(radius * 2, 0) });
                        break;
                }
                if (Player.Invulnerable)
                {
                    power.Stroke = Brushes.Red;
                }
                else
                {
                    power.Stroke = Brushes.White;
                }

                power.Clip = new PathGeometry() { Figures = new PathFigureCollection(new PathFigure[] { new PathFigure(point1, new PathSegment[] { clipPolygon }, false) }) };
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            Timer = new DispatcherTimer(DispatcherPriority.Send);
            Timer.Interval = TimeSpan.FromSeconds(0.001);
            Timer.Tick += OnTick;
            Timer.Start();

            BlocksToRemove = new List<Block>();

            CreateBlocks(200);
            CurrentBlockIndex = 0;

            VisualizedBlocks = new List<Block>();
            for (int index = 0; index < BlocksBunch; index++)
            {
                VisualizedBlocks.Add(Blocks[index]);
                viewport.Children.Add(Blocks[index].GetZones());
            }

            Player = new PlayerBall(new Point3D(0, PlayerBall.BallSettings.Item1 / 2 + Block.BlockSettings.Item1, Block.BlockSettings.Item2 - Block.BlockSettings.Item3 / 2), CurrentBlock);
            viewport.Children.Add(new ModelVisual3D() { Content = new Model3DGroup() { Children = Player.Ball } });

            Player.OnHit = HitBlock;
            Player.OnDie = OnDie;

            viewport.Camera.SetValue(ProjectionCamera.FarPlaneDistanceProperty, ZDepth * 2);
            viewport.Camera.SetValue(ProjectionCamera.PositionProperty, new Point3D(0, 0, ZDepth));
        }

        private void OnTick(object sender, EventArgs e)
        {
            SetBallState();

            AnimateBlocks();

            RemoveBlocks();

            Player.Update();

            AnimatePower();

            debug.Content = Player.CanHit();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Hitting = true;
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Hitting = false;
            if (Player.HittedCount != 0)
            {
                CurrentBlock.Scale();
            }
        }
    }
}
