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
        Hitting, Jumping
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
    }

    public class PlayerBall
    {
        private Model3DCollection Ball
        {
            get; set;
        }
        /// <summary>
        /// Radius count
        /// </summary>
        public readonly (double, int) BlockSettings = (1, 100);
        public BallState State
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

                createVertices(1);
                getIndices(0);

                return create3DObject(vertices, indices);
            };


            parts[0] = getHalfSphere(1);
            parts[1] = getHalfSphere(-1);

            return parts;
        }
        public PlayerBall()
        {
            Ball = GetSphere(BlockSettings.Item1, BlockSettings.Item2).Merge(new DiffuseMaterial(Brushes.DodgerBlue));
        }
    }

    public class Block
    {
        private double OffsetY;
        private bool Disposing;
        private const double DisposeRadius = 0.25;
        private const double AngleDelta = 1;
        private double Angle;
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
        private double ToRad(double angle)
        {
            return angle / 180 * Math.PI;
        }
        private void CreateGraphics()
        {
            DeadZonesGeometry = new List<Model3DCollection>();
            HealthZonesGeometry = new List<Model3DCollection>();

            Func<double, double, Brush, Model3DCollection> createZone = (double startAngle, double sweepAngle, Brush color) =>
               {
                   var arc = GetArc(BlockSettings.Item1, BlockSettings.Item2, ToRad(startAngle), ToRad(sweepAngle), BlockSettings.Item3, BlockSettings.Item4);
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
                    obj.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), angle));
                }
            }
        }
        private void Translate(Model3DCollection collection, Vector3D offset)
        {
            foreach (var element in collection)
            {
                element.Transform = element.Transform.AddTransform(new TranslateTransform3D(offset.X, offset.Y, offset.Z));
            }
        }

        public readonly SolidColorBrush HealthZoneColor = Brushes.LawnGreen;
        public readonly SolidColorBrush DeadZoneColor = Brushes.DarkRed;
        /// <summary>
        /// Height, radius, width, count
        /// </summary>
        public readonly (double, double, double, int) BlockSettings = (0.2, 2, 0.2, 100);

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
                    var angle = 360 - ToRad(Angle + zone.Item1 + zone.Item2 / 2);
                    var disposeDirection = new Vector3D(Math.Cos(angle) * DisposeRadius, 0.5 * DisposeRadius, Math.Sin(angle) * DisposeRadius);
                    Translate(HealthZonesGeometry[index], disposeDirection);
                }
                for (int index = 0; index < DeadZones.Length; index++)
                {
                    var zone = DeadZones[index];
                    var angle = 360 - ToRad(Angle + zone.Item1 + zone.Item2 / 2);
                    var disposeDirection = new Vector3D(Math.Cos(angle) * DisposeRadius, 0.5 * DisposeRadius, Math.Sin(angle) * DisposeRadius);
                    Translate(DeadZonesGeometry[index], disposeDirection);
                }
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
        DispatcherTimer Timer;
        List<Block> Blocks;

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
                    var block = new Block((count - remainCount + index) * -0.3);
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

        public MainWindow()
        {
            InitializeComponent();

            Timer = new DispatcherTimer(DispatcherPriority.Send);
            Timer.Interval = TimeSpan.FromSeconds(0.001);
            Timer.Tick += OnTick;
            Timer.Start();

            CreateBlocks(5);
        }

        private void OnTick(object sender, EventArgs e)
        {
            foreach (var block in Blocks)
            {
                block.Update();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Blocks[0].Dispose();
        }
    }
}
