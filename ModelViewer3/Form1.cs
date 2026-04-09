using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ModelViewer3
{
    public partial class Form1 : Form
    {
        private PictureBox renderBox = null!;
        private Button btnLoad = null!;
        private Bitmap renderTarget = null!;
        
        private List<Vector4> vertices = new List<Vector4>();
        private List<Vector3> vertexNormals = new List<Vector3>();
        private List<int[]> faces = new List<int[]>();
        private VertexData[] projectedBuffer = Array.Empty<VertexData>();
        private float[] zBuffer = Array.Empty<float>();

        private float angleX = 0, angleY = 0, cameraDistance = 5f;
        private Point lastMousePos;
        private bool isDragging = false;

        private Vector3 lightDir = new Vector3(0, 0, 1).Normalize();
        private Color objectColor = Color.Orange;
        private float Ka = 0.15f; // Фоновое
        private float Kd = 0.7f;  // Рассеянное
        private float Ks = 0.5f;  // Бликовое
        private float shininess = 32f; // Коэффициент блеска

        public Form1()
        {
            InitializeComponentCustom();
            Application.Idle += (s, e) => { if (vertices.Count > 0) Render(); };
        }

        private void InitializeComponentCustom()
        {
            this.Text = "Model Viewer - Lab 3 (Phong Shading)";
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
                    projectedBuffer = new VertexData[vertices.Count];
                    CalculateVertexNormals();
                }
            }
        }

        // Аппроксимация нормалей вершин (усреднение нормалей смежных граней)
        private void CalculateVertexNormals()
        {
            Vector3[] normals = new Vector3[vertices.Count];
            foreach (var face in faces) {
                if (face.Length < 3) continue;
                Vector3 v0 = new Vector3(vertices[face[0]].X, vertices[face[0]].Y, vertices[face[0]].Z);
                Vector3 v1 = new Vector3(vertices[face[1]].X, vertices[face[1]].Y, vertices[face[1]].Z);
                Vector3 v2 = new Vector3(vertices[face[2]].X, vertices[face[2]].Y, vertices[face[2]].Z);
                
                Vector3 edge1 = Vector3.Subtract(v1, v0);
                Vector3 edge2 = Vector3.Subtract(v2, v0);
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);

                for (int i = 0; i < face.Length; i++) {
                    normals[face[i]] = Vector3.Add(normals[face[i]], faceNormal);
                }
            }

            vertexNormals = new List<Vector3>(vertices.Count);
            for (int i = 0; i < normals.Length; i++) {
                vertexNormals.Add(normals[i].Normalize());
            }
        }

        private void Render()
        {
            int w = renderTarget.Width, h = renderTarget.Height;
            Vector3 cameraPos = new Vector3(0, 0, cameraDistance);
            
            Matrix4x4 matWorld = Matrix4x4.Multiply(Matrix4x4.RotateX(angleX), Matrix4x4.RotateY(angleY));
            Matrix4x4 matView = Matrix4x4.LookAt(cameraPos, new Vector3(0, 0, 0), new Vector3(0, 1, 0));
            Matrix4x4 matProj = Matrix4x4.PerspectiveFOV((float)Math.PI / 3f, (float)w / h, 0.1f, 100f);
            Matrix4x4 matVP = Matrix4x4.Viewport(w, h);
            Matrix4x4 transform = Matrix4x4.Multiply(matVP, Matrix4x4.Multiply(matProj, Matrix4x4.Multiply(matView, matWorld)));

            Parallel.For(0, vertices.Count, i => {
                // Трансформация вершин
                Vector4 wPos = Matrix4x4.Multiply(matWorld, vertices[i]);
                Vector4 sPos = Matrix4x4.Multiply(transform, vertices[i]);
                if (sPos.W != 0) { sPos.X /= sPos.W; sPos.Y /= sPos.W; sPos.Z /= sPos.W; }
                
                // Трансформация нормалей (т.к. у нас только повороты, можно использовать matWorld напрямую)
                Vector4 nPos = Matrix4x4.Multiply(matWorld, new Vector4(vertexNormals[i].X, vertexNormals[i].Y, vertexNormals[i].Z, 0));
                
                projectedBuffer[i] = new VertexData {
                    ScreenPos = sPos,
                    WorldPos = new Vector3(wPos.X, wPos.Y, wPos.Z),
                    Normal = new Vector3(nPos.X, nPos.Y, nPos.Z).Normalize()
                };
            });

            BitmapData data = renderTarget.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, renderTarget.PixelFormat);
            unsafe {
                int* ptr = (int*)data.Scan0;
                int bg = unchecked((int)0xFF121212);
                
                Parallel.For(0, h, y => {
                    int* row = ptr + (y * w);
                    int zOffset = y * w;
                    for (int x = 0; x < w; x++) {
                        row[x] = bg;
                        zBuffer[zOffset + x] = float.MaxValue;
                    }
                });

                foreach (var face in faces) {
                    if (face.Length < 3) continue;

                    Vector3 v0w = projectedBuffer[face[0]].WorldPos;
                    Vector3 v1w = projectedBuffer[face[1]].WorldPos;
                    Vector3 v2w = projectedBuffer[face[2]].WorldPos;

                    Vector3 edge1 = Vector3.Subtract(v1w, v0w);
                    Vector3 edge2 = Vector3.Subtract(v2w, v0w);
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2).Normalize();
                    Vector3 viewDir = Vector3.Subtract(cameraPos, v0w).Normalize();

                    // Отбраковка невидимых поверхностей (на основе нормали грани)
                    if (Vector3.Dot(faceNormal, viewDir) >= 0) {
                        for (int i = 1; i < face.Length - 1; i++) {
                            FillTrianglePhong(projectedBuffer[face[0]], projectedBuffer[face[i]], projectedBuffer[face[i + 1]], ptr, w, h, cameraPos);
                        }
                    }
                }
            }
            renderTarget.UnlockBits(data);
            renderBox.Image = renderTarget;
        }

        // Растеризация с интерполяцией нормалей (Затенение Гуро/Фонга)
        private unsafe void FillTrianglePhong(VertexData p0, VertexData p1, VertexData p2, int* ptr, int w, int h, Vector3 cameraPos)
        {
            if (p0.ScreenPos.Y > p1.ScreenPos.Y) Swap(ref p0, ref p1);
            if (p0.ScreenPos.Y > p2.ScreenPos.Y) Swap(ref p0, ref p2);
            if (p1.ScreenPos.Y > p2.ScreenPos.Y) Swap(ref p1, ref p2);

            int total_height = (int)(p2.ScreenPos.Y - p0.ScreenPos.Y);
            if (total_height == 0) return;

            for (int i = 0; i < total_height; i++) {
                bool second_half = i > p1.ScreenPos.Y - p0.ScreenPos.Y || p1.ScreenPos.Y == p0.ScreenPos.Y;
                int segment_height = second_half ? (int)(p2.ScreenPos.Y - p1.ScreenPos.Y) : (int)(p1.ScreenPos.Y - p0.ScreenPos.Y);
                if (segment_height == 0) continue;

                float alpha = (float)i / total_height;
                float beta  = (float)(i - (second_half ? p1.ScreenPos.Y - p0.ScreenPos.Y : 0)) / segment_height;

                VertexData A = VertexData.Lerp(p0, p2, alpha);
                VertexData B = second_half ? VertexData.Lerp(p1, p2, beta) : VertexData.Lerp(p0, p1, beta);

                if (A.ScreenPos.X > B.ScreenPos.X) Swap(ref A, ref B);

                int y = (int)p0.ScreenPos.Y + i;
                if (y < 0 || y >= h) continue;

                int x_start = Math.Max(0, (int)A.ScreenPos.X);
                int x_end = Math.Min(w - 1, (int)B.ScreenPos.X);

                for (int x = x_start; x <= x_end; x++) {
                    float phi = B.ScreenPos.X == A.ScreenPos.X ? 1.0f : (float)(x - A.ScreenPos.X) / (float)(B.ScreenPos.X - A.ScreenPos.X);
                    
                    // Попиксельная интерполяция данных
                    VertexData P = VertexData.Lerp(A, B, phi);
                    int idx = y * w + x;
                    
                    if (P.ScreenPos.Z < zBuffer[idx]) {
                        zBuffer[idx] = P.ScreenPos.Z;
                        ptr[idx] = CalculatePhongLight(P.WorldPos, P.Normal.Normalize(), cameraPos);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CalculatePhongLight(Vector3 worldPos, Vector3 normal, Vector3 cameraPos)
        {
            // Направление взгляда (V)
            Vector3 viewDir = Vector3.Subtract(cameraPos, worldPos).Normalize();
            
            // 1. Фоновое освещение (Ambient)
            float ambient = Ka;

            // 2. Рассеянное освещение (Diffuse)
            float diff = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
            float diffuse = Kd * diff;

            // 3. Зеркальное освещение (Specular)
            float specular = 0.0f;
            if (diff > 0.0f) {
                // Вектор отражения: R = 2 * (N * L) * N - L
                Vector3 reflectDir = Vector3.Subtract(Vector3.Multiply(normal, 2.0f * Vector3.Dot(normal, lightDir)), lightDir).Normalize();
                float spec = (float)Math.Pow(Math.Max(Vector3.Dot(viewDir, reflectDir), 0.0f), shininess);
                specular = Ks * spec;
            }

            // Суммируем компоненты: I = Ia + Id + Is
            float intensity = Math.Min(ambient + diffuse + specular, 1.0f);

            int r = (int)(objectColor.R * intensity);
            int g = (int)(objectColor.G * intensity);
            int b = (int)(objectColor.B * intensity);
            
            // Добавляем белый цвет блика (цвет света) для реалистичности
            r = Math.Min(r + (int)(255 * specular), 255);
            g = Math.Min(g + (int)(255 * specular), 255);
            b = Math.Min(b + (int)(255 * specular), 255);

            return unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
        }

        private void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }
    }

    #region Math & Structs
    public struct VertexData {
        public Vector4 ScreenPos;
        public Vector3 WorldPos;
        public Vector3 Normal;

        public static VertexData Lerp(VertexData a, VertexData b, float t) {
            return new VertexData {
                ScreenPos = Vector4.Lerp(a.ScreenPos, b.ScreenPos, t),
                WorldPos = Vector3.Lerp(a.WorldPos, b.WorldPos, t),
                Normal = Vector3.Lerp(a.Normal, b.Normal, t)
            };
        }
    }

    public struct Vector3 {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 Multiply(Vector3 a, float d) => new Vector3(a.X * d, a.Y * d, a.Z * d);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public Vector3 Normalize() { float l = (float)Math.Sqrt(X * X + Y * Y + Z * Z); return l == 0 ? this : new Vector3(X / l, Y / l, Z / l); }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }
    public struct Vector4 { 
        public float X, Y, Z, W; 
        public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; } 
        public static Vector4 Lerp(Vector4 v1, Vector4 v2, float t) => new Vector4(v1.X + (v2.X - v1.X) * t, v1.Y + (v2.Y - v1.Y) * t, v1.Z + (v2.Z - v1.Z) * t, 1);
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