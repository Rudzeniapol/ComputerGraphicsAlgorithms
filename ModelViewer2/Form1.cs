using System.Drawing.Imaging;
using System.Globalization;

namespace ModelViewer2
{
    public partial class Form1 : Form
    {
        private PictureBox renderBox = null!;
        private Button btnLoad = null!;
        private Bitmap renderTarget = null!;
        
        private List<Vector4> vertices = new List<Vector4>();
        private List<int[]> faces = new List<int[]>();
        private Vector4[] projectedBuffer = Array.Empty<Vector4>();
        private float[] zBuffer = Array.Empty<float>(); // Добавлен Z-буфер

        private float angleX = 0, angleY = 0, cameraDistance = 5f;
        private Point lastMousePos;
        private bool isDragging = false;

        public Form1()
        {
            InitializeComponentCustom();
            Application.Idle += (s, e) => { if (vertices.Count > 0) Render(); };
        }

        private void InitializeComponentCustom()
        {
            this.Text = "Model Viewer - Lab 2";
            this.Size = new Size(1024, 768);
            this.DoubleBuffered = true;

            btnLoad = new Button { 
                Text = "Load OBJ", 
                Location = new Point(10, 10), 
                BackColor = Color.White,
                Width = 100 
            };
            btnLoad.Click += (s, e) => LoadObj();

            renderBox = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black };
            renderBox.MouseDown += (s, e) => { isDragging = true; lastMousePos = e.Location; };
            renderBox.MouseUp += (s, e) => isDragging = false;
            renderBox.MouseMove += (s, e) => {
                if (isDragging) {
                    angleY += (e.X - lastMousePos.X) * 0.01f;
                    angleX += (e.Y - lastMousePos.Y) * 0.01f;
                    lastMousePos = e.Location;
                }   
            };
            this.MouseWheel += (s, e) => {
                cameraDistance -= e.Delta * 0.005f;
                if (cameraDistance < 0.1f) cameraDistance = 0.1f;
            };

            renderBox.SizeChanged += (s, e) => CreateRenderTarget();
            this.Controls.Add(btnLoad);
            this.Controls.Add(renderBox);
            btnLoad.BringToFront();
            CreateRenderTarget();
        }

        private void CreateRenderTarget()
        {
            if (renderBox.Width > 0 && renderBox.Height > 0) {
                renderTarget?.Dispose();
                renderTarget = new Bitmap(renderBox.Width, renderBox.Height, PixelFormat.Format32bppPArgb);
                zBuffer = new float[renderBox.Width * renderBox.Height];
            }
        }

        private void LoadObj()
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "OBJ Files|*.obj" }) {
                if (ofd.ShowDialog() == DialogResult.OK) {
                    var tempV = new List<Vector4>();
                    var tempF = new List<int[]>();
                    foreach (var line in File.ReadLines(ofd.FileName)) {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length == 0) continue;
                        if (p[0] == "v") tempV.Add(new Vector4(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture), 1f));
                        else if (p[0] == "f") {
                            int[] f = new int[p.Length - 1];
                            for (int i = 1; i < p.Length; i++) f[i - 1] = int.Parse(p[i].Split('/')[0]) - 1;
                            tempF.Add(f);
                        }
                    }
                    vertices = tempV;
                    faces = tempF;
                    projectedBuffer = new Vector4[vertices.Count];
                }
            }
        }

        private void Render()
        {
            int w = renderTarget.Width, h = renderTarget.Height;
            Vector3 cameraPos = new Vector3(0, 0, cameraDistance);
            Vector3 lightDir = new Vector3(0, 0, 1).Normalize(); // Вектор направления К источнику света (для Ламберта)
            Color baseColor = Color.Orange; // Базовый цвет модели
            
            Matrix4x4 matWorld = Matrix4x4.Multiply(Matrix4x4.RotateX(angleX), Matrix4x4.RotateY(angleY));
            Matrix4x4 matView = Matrix4x4.LookAt(cameraPos, new Vector3(0, 0, 0), new Vector3(0, 1, 0));
            Matrix4x4 matProj = Matrix4x4.PerspectiveFOV((float)Math.PI / 3f, (float)w / h, 0.1f, 100f);
            Matrix4x4 matVP = Matrix4x4.Viewport(w, h);
            Matrix4x4 transform = Matrix4x4.Multiply(matVP, Matrix4x4.Multiply(matProj, Matrix4x4.Multiply(matView, matWorld)));

            Parallel.For(0, vertices.Count, i => {
                Vector4 v = Matrix4x4.Multiply(transform, vertices[i]);
                if (v.W != 0) { v.X /= v.W; v.Y /= v.W; v.Z /= v.W; }
                projectedBuffer[i] = v;
            });

            BitmapData data = renderTarget.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, renderTarget.PixelFormat);
            unsafe {
                int* ptr = (int*)data.Scan0;
                int bg = unchecked((int)0xFF121212);
                
                // Очистка экрана и Z-буфера
                Parallel.For(0, h, y => {
                    int* row = ptr + (y * w);
                    int zOffset = y * w;
                    for (int x = 0; x < w; x++) {
                        row[x] = bg;
                        zBuffer[zOffset + x] = float.MaxValue;
                    }
                });

                // Отрисовка треугольников (последовательно для корректной работы Z-буфера)
                foreach (var face in faces) {
                    if (face.Length < 3) continue;

                    Vector4 v0w = Matrix4x4.Multiply(matWorld, vertices[face[0]]);
                    Vector4 v1w = Matrix4x4.Multiply(matWorld, vertices[face[1]]);
                    Vector4 v2w = Matrix4x4.Multiply(matWorld, vertices[face[2]]);

                    Vector3 edge1 = new Vector3(v1w.X - v0w.X, v1w.Y - v0w.Y, v1w.Z - v0w.Z);
                    Vector3 edge2 = new Vector3(v2w.X - v0w.X, v2w.Y - v0w.Y, v2w.Z - v0w.Z);
                    Vector3 normal = Vector3.Cross(edge1, edge2).Normalize();
                    
                    // Вектор взгляда (от поверхности к камере)
                    Vector3 viewDir = new Vector3(cameraPos.X - v0w.X, cameraPos.Y - v0w.Y, cameraPos.Z - v0w.Z).Normalize();

                    // Отбраковка невидимых поверхностей
                    if (Vector3.Dot(normal, viewDir) >= 0) {
                        
                        // Плоское затенение (модель Ламберта)
                        float intensity = Math.Max(0.1f, Vector3.Dot(normal, lightDir)); // 0.1f - фоновое (ambient) освещение
                        int r = (int)(baseColor.R * intensity);
                        int g = (int)(baseColor.G * intensity);
                        int b = (int)(baseColor.B * intensity);
                        int faceColor = unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);

                        // Триангуляция многоугольников (если вершин > 3)
                        for (int i = 1; i < face.Length - 1; i++) {
                            Vector4 p0 = projectedBuffer[face[0]];
                            Vector4 p1 = projectedBuffer[face[i]];
                            Vector4 p2 = projectedBuffer[face[i + 1]];
                            
                            FillTriangle(p0, p1, p2, faceColor, ptr, w, h);
                        }
                    }
                }
            }
            renderTarget.UnlockBits(data);
            renderBox.Image = renderTarget;
        }

        // Алгоритм растеризации треугольника (метод сканирующей линии) с использованием Z-буфера
        private unsafe void FillTriangle(Vector4 p0, Vector4 p1, Vector4 p2, int color, int* ptr, int w, int h)
        {
            // Сортировка вершин по Y (сверху вниз)
            if (p0.Y > p1.Y) Swap(ref p0, ref p1);
            if (p0.Y > p2.Y) Swap(ref p0, ref p2);
            if (p1.Y > p2.Y) Swap(ref p1, ref p2);

            int total_height = (int)(p2.Y - p0.Y);
            if (total_height == 0) return;

            // Разбиваем треугольник на верхнюю и нижнюю части
            for (int i = 0; i < total_height; i++) {
                bool second_half = i > p1.Y - p0.Y || p1.Y == p0.Y;
                int segment_height = second_half ? (int)(p2.Y - p1.Y) : (int)(p1.Y - p0.Y);
                if (segment_height == 0) continue;

                float alpha = (float)i / total_height;
                float beta  = (float)(i - (second_half ? p1.Y - p0.Y : 0)) / segment_height;

                Vector4 A = Vector4.Lerp(p0, p2, alpha);
                Vector4 B = second_half ? Vector4.Lerp(p1, p2, beta) : Vector4.Lerp(p0, p1, beta);

                if (A.X > B.X) Swap(ref A, ref B);

                int y = (int)p0.Y + i;
                if (y < 0 || y >= h) continue;

                int x_start = Math.Max(0, (int)A.X);
                int x_end = Math.Min(w - 1, (int)B.X);

                for (int x = x_start; x <= x_end; x++) {
                    float phi = B.X == A.X ? 1.0f : (float)(x - A.X) / (float)(B.X - A.X);
                    float z = A.Z + (B.Z - A.Z) * phi;
                    
                    int idx = y * w + x;
                    
                    // Проверка Z-буфера
                    if (z < zBuffer[idx]) {
                        zBuffer[idx] = z;
                        ptr[idx] = color;
                    }
                }
            }
        }

        private void Swap<T>(ref T a, ref T b) {
            T temp = a;
            a = b;
            b = temp;
        }
    }

    #region Math
    public struct Vector3 {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public Vector3 Normalize() { float l = (float)Math.Sqrt(X * X + Y * Y + Z * Z); return l == 0 ? this : new Vector3(X / l, Y / l, Z / l); }
    }
    public struct Vector4 { 
        public float X, Y, Z, W; 
        public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; } 
        // Метод линейной интерполяции для расчета координат на краях треугольника
        public static Vector4 Lerp(Vector4 v1, Vector4 v2, float t) {
            return new Vector4(v1.X + (v2.X - v1.X) * t, v1.Y + (v2.Y - v1.Y) * t, v1.Z + (v2.Z - v1.Z) * t, 1);
        }
    }
    public class Matrix4x4 {
        public float[,] M = new float[4, 4];
        public static Matrix4x4 Multiply(Matrix4x4 a, Matrix4x4 b) {
            var r = new Matrix4x4();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) for (int k = 0; k < 4; k++) r.M[i, j] += a.M[i, k] * b.M[k, j];
            return r;
        }
        public static Vector4 Multiply(Matrix4x4 m, Vector4 v) => new Vector4(
            m.M[0,0]*v.X + m.M[0,1]*v.Y + m.M[0,2]*v.Z + m.M[0,3]*v.W,
            m.M[1,0]*v.X + m.M[1,1]*v.Y + m.M[1,2]*v.Z + m.M[1,3]*v.W,
            m.M[2,0]*v.X + m.M[2,1]*v.Y + m.M[2,2]*v.Z + m.M[2,3]*v.W,
            m.M[3,0]*v.X + m.M[3,1]*v.Y + m.M[3,2]*v.Z + m.M[3,3]*v.W);
        public static Matrix4x4 RotateX(float a) { var r = new Matrix4x4(); float c = (float)Math.Cos(a), s = (float)Math.Sin(a); r.M[0,0]=1; r.M[1,1]=c; r.M[1,2]=-s; r.M[2,1]=s; r.M[2,2]=c; r.M[3,3]=1; return r; }
        public static Matrix4x4 RotateY(float a) { var r = new Matrix4x4(); float c = (float)Math.Cos(a), s = (float)Math.Sin(a); r.M[1,1]=1; r.M[0,0]=c; r.M[0,2]=s; r.M[2,0]=-s; r.M[2,2]=c; r.M[3,3]=1; return r; }
        public static Matrix4x4 LookAt(Vector3 eye, Vector3 target, Vector3 up) {
            Vector3 z = Vector3.Subtract(eye, target).Normalize();
            Vector3 x = Vector3.Cross(up, z).Normalize();
            Vector3 y = Vector3.Cross(z, x).Normalize();
            var r = new Matrix4x4();
            r.M[0,0]=x.X; r.M[0,1]=x.Y; r.M[0,2]=x.Z; r.M[0,3]=-Vector3.Dot(x, eye);
            r.M[1,0]=y.X; r.M[1,1]=y.Y; r.M[1,2]=y.Z; r.M[1,3]=-Vector3.Dot(y, eye);
            r.M[2,0]=z.X; r.M[2,1]=z.Y; r.M[2,2]=z.Z; r.M[2,3]=-Vector3.Dot(z, eye);
            r.M[3,3]=1; return r;
        }
        public static Matrix4x4 PerspectiveFOV(float fov, float asp, float n, float f) {
            var r = new Matrix4x4(); float h = 1f / (float)Math.Tan(fov/2);
            r.M[0,0]=h/asp; r.M[1,1]=h; r.M[2,2]=f/(n-f); r.M[2,3]=(n*f)/(n-f); r.M[3,2]=-1; return r;
        }
        public static Matrix4x4 Viewport(int w, int h) {
            var r = new Matrix4x4(); r.M[0,0]=w/2f; r.M[0,3]=w/2f; r.M[1,1]=-h/2f; r.M[1,3]=h/2f; r.M[2,2]=1; r.M[3,3]=1; return r;
        }
    }
    #endregion
}