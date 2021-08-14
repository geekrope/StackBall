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
        private const int JumpHeight = 1;
        private const int JumpFrames = 40;
        /// <summary>
        /// Radius count
        /// </summary>
        public static readonly (double, int) BallSettings = (0.2, 100);
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
        public Action OnDied
        {
            get; set;
        }

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
            foreach (var obj in Ball)
            {
                obj.Transform = new TranslateTransform3D(OriginalPosition.X, jumpOffset + OriginalPosition.Y + OffsetY, OriginalPosition.Z);
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
        private bool CanHit()
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
                if (CanHit())
                {
                    OnHit?.Invoke();
                }
                else
                {
                    OnDied?.Invoke();
                }
            }
        }
    }

    public class Block
    {
        private bool Disposing;
        private double DisposeRadius = 0;
        private const double DisposeRadiusDelta = 0.2;
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

                    verticesTop[index] = new Point3D(x, height / 2 + OffsetY, z);
                    verticesTop[index + 1] = new Point3D(x2, height / 2 + OffsetY, z2);

                    verticesBottom[index] = new Point3D(x, -height / 2 + OffsetY, z);
                    verticesBottom[index + 1] = new Point3D(x2, -height / 2 + OffsetY, z2);

                    verticesFront[index] = new Point3D(x, -height / 2 + OffsetY, z);
                    verticesFront[index + 1] = new Point3D(x, height / 2 + OffsetY, z);

                    verticesBack[index] = new Point3D(x2, -height / 2 + OffsetY, z2);
                    verticesBack[index + 1] = new Point3D(x2, height / 2 + OffsetY, z2);
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
                    obj.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -angle));
                }
            }
        }
        private void Translate(Model3DCollection collection, Vector3D offset)
        {
            foreach (var element in collection)
            {
                //element.Transform.Value.Append(new Matrix3D() { OffsetX = offset.X, OffsetY = offset.Y, OffsetZ = offset.Z });
                //element.Transform = element.Transform.AddTransform(new TranslateTransform3D(offset.X, offset.Y, offset.Z));
                element.Transform = new Transform3DGroup() { Children = new Transform3DCollection(new Transform3D[] { new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -Angle)), new TranslateTransform3D(offset.X, offset.Y, offset.Z) }) };
            }
        }

        public static readonly SolidColorBrush HealthZoneColor = Brushes.LawnGreen;
        public static readonly SolidColorBrush DeadZoneColor = Brushes.DarkRed;
        /// <summary>
        /// Height, radius, width, count
        /// </summary>
        public static readonly (double, double, double, int) BlockSettings = (0.2, 2, 0.5, 100);

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
                        currentAngle = deadZones[index].Item1 + deadZones[index].Item2;
                        healthZones.Add((angle, angle2 - angle));
                    }

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
            get; private set;
        }
        public double Angle
        {
            get; private set;
        }

        public Model3DGroup GetZones()
        {
            var result = new Model3DCollection(DeadZonesGeometry.ExpandCollection().Concat(HealthZonesGeometry.ExpandCollection()));

            return new Model3DGroup() { Children = result };
        }
        public void Update()
        {
            if (!Disposing)
            {
                Rotate(DeadZonesGeometry, Angle);
                Rotate(HealthZonesGeometry, Angle);

                Angle += AngleDelta;
                Angle = Angle % 360;
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
            }
        }
        public void Dispose()
        {
            Disposing = true;
        }

        public Block(double offset)
        {
            OffsetY = offset;
        }
    }
    public partial class MainWindow : Window
    {
        const double BlockSpot = 0.3;
        DispatcherTimer Timer;
        List<Block> Blocks;
        PlayerBall Ball;
        Block CurrentBlock;

        private void CreateBlocks(int count)
        {
            Blocks = new List<Block>();

            var remainCount = count;
            var random = new Random();
            for (; remainCount > 0;)
            {
                var bunch = 0;

                if (remainCount > 2)
                {
                    bunch = random.Next(2, remainCount);
                }
                else
                {
                    bunch = 1;
                }

                for (int index = 0; index < bunch; index++)
                {
                    var block = new Block((count - remainCount + index) * -BlockSpot);
                    if (random.Next(0, 2) == 0)
                    {
                        var deadZone = (random.Next(0, 20), random.Next(40, 60));
                        block.DeadZones = new (int, int)[] { deadZone };
                    }
                    else
                    {
                        var deadZone = (random.Next(0, 20), random.Next(40, 60));
                        var deadZone2 = (random.Next(100, 130), random.Next(160, 200));
                        block.DeadZones = new (int, int)[] { deadZone, deadZone2 };
                    }

                    viewport.Children.Add(new ModelVisual3D() { Content = block.GetZones() });
                    Blocks.Add(block);
                }

                remainCount -= bunch;
            }
        }

        private void HitBlock()
        {
            var index = Blocks.IndexOf(CurrentBlock);
            CurrentBlock.Dispose();
            if (index + 1 < Blocks.Count)
            {
                CurrentBlock = Blocks[index + 1];
            }
            Ball.CurrentBlock = CurrentBlock;
        }

        public MainWindow()
        {
            InitializeComponent();

            Timer = new DispatcherTimer(DispatcherPriority.Send);
            Timer.Interval = TimeSpan.FromSeconds(0.001);
            Timer.Tick += OnTick;
            Timer.Start();

            CreateBlocks(50);
            CurrentBlock = Blocks[0];

            Ball = new PlayerBall(new Point3D(0, PlayerBall.BallSettings.Item1 / 2 + BlockSpot / 2, Block.BlockSettings.Item2 - Block.BlockSettings.Item3 / 2), CurrentBlock);
            viewport.Children.Add(new ModelVisual3D() { Content = new Model3DGroup() { Children = Ball.Ball } });

            Ball.OnHit = HitBlock;
        }

        private void OnTick(object sender, EventArgs e)
        {
            foreach (var block in Blocks)
            {
                block.Update();
            }
            Ball.Update();

            var position = (Point3D)viewport.Camera.GetValue(ProjectionCamera.PositionProperty);
            if (position.Y > CurrentBlock.OffsetY)
            {
                var delta = Math.Max(CurrentBlock.OffsetY - position.Y, -0.05);
                viewport.Camera.SetValue(ProjectionCamera.PositionProperty, new Point3D(position.X, position.Y + delta, position.Z));
                Ball.OffsetY += delta;
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Ball.Hit();
        }
    }
}
