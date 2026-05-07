using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ModelViewer4
{
    public partial class Form1 : Form
    {
        private PictureBox renderBox = null!;
        private Button btnLoadObj = null!, btnLoadDiffuse = null!, btnLoadNormal = null!, btnLoadSpecular = null!;
        private Bitmap renderTarget = null!;
        
        private List<Vector4> vertices = new List<Vector4>();
        private List<Vector2> uvs = new List<Vector2>();
        private List<Vector3> vertexNormals = new List<Vector3>();
        private List<FaceVertex[]> faces = new List<FaceVertex[]>();
        private float[] zBuffer = Array.Empty<float>();

        private Texture? texDiffuse = null;
        private Texture? texNormal = null;
        private Texture? texSpecular = null;

        private float angleX = 0, angleY = 0, cameraDistance = 5f;
        private Point lastMousePos;
        private bool isDragging = false;

        private Vector3 lightDir = new Vector3(0, 0, 1).Normalize();
        private Color objectColor = Color.Orange;
        private float Ka = 0.15f; 
        private float Kd = 0.7f;  
        private float Ks = 0.5f;  
        private float shininess = 32f; 

        public Form1()
        {
            InitializeComponentCustom();
            Application.Idle += (s, e) => { if (vertices.Count > 0) Render(); };
        }

        private void InitializeComponentCustom()
        {
            this.Text = "Model Viewer - Lab 4 (Textures & Normal Maps)";
            this.Size = new Size(1024, 768);
            this.DoubleBuffered = true;

            // Общие параметры для высоты кнопок
            int btnHeight = 35;

            btnLoadObj = new Button { 
                Text = "Load OBJ", 
                Location = new Point(10, 10), 
                BackColor = Color.White, 
                Width = 100, 
                Height = btnHeight,
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            btnLoadDiffuse = new Button { 
                Text = "+ Diffuse Map", 
                Location = new Point(120, 10), 
                BackColor = Color.LightGray, 
                Width = 100, 
                Height = btnHeight,
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            btnLoadNormal = new Button { 
                Text = "+ Normal Map", 
                Location = new Point(230, 10), 
                BackColor = Color.LightGray, 
                Width = 100, 
                Height = btnHeight,
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            btnLoadSpecular = new Button { 
                Text = "+ Specular Map", 
                Location = new Point(340, 10), 
                BackColor = Color.LightGray, 
                Width = 100, 
                Height = btnHeight,
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnLoadObj.Click += (s, e) => LoadObj();
            btnLoadDiffuse.Click += (s, e) => LoadTexture(ref texDiffuse, btnLoadDiffuse);
            btnLoadNormal.Click += (s, e) => LoadTexture(ref texNormal, btnLoadNormal);
            btnLoadSpecular.Click += (s, e) => LoadTexture(ref texSpecular, btnLoadSpecular);

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
            
            this.Controls.Add(btnLoadObj);
            this.Controls.Add(btnLoadDiffuse);
            this.Controls.Add(btnLoadNormal);
            this.Controls.Add(btnLoadSpecular);
            this.Controls.Add(renderBox);
            
            btnLoadObj.BringToFront(); 
            btnLoadDiffuse.BringToFront(); 
            btnLoadNormal.BringToFront(); 
            btnLoadSpecular.BringToFront();
            
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
                    vertices.Clear(); uvs.Clear(); faces.Clear();
                    
                    foreach (var line in File.ReadLines(ofd.FileName)) {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length == 0) continue;
                        
                        if (p[0] == "v") vertices.Add(new Vector4(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture), 1f));
                        else if (p[0] == "vt") uvs.Add(new Vector2(float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture)));
                        else if (p[0] == "f") {
                            FaceVertex[] f = new FaceVertex[p.Length - 1];
                            for (int i = 1; i < p.Length; i++) {
                                string[] parts = p[i].Split('/');
                                f[i - 1].V = int.Parse(parts[0]) - 1;
                                f[i - 1].Vt = (parts.Length > 1 && parts[1].Length > 0) ? int.Parse(parts[1]) - 1 : -1;
                                f[i - 1].Vn = (parts.Length > 2 && parts[2].Length > 0) ? int.Parse(parts[2]) - 1 : -1;
                            }
                            faces.Add(f);
                        }
                    }
                    CalculateVertexNormals();
                }
            }
        }

        private void LoadTexture(ref Texture? tex, Button btn)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" }) {
                if (ofd.ShowDialog() == DialogResult.OK) {
                    tex = new Texture(ofd.FileName);
                    btn.BackColor = Color.LightGreen;
                }
            }
        }

        private void CalculateVertexNormals()
        {
            Vector3[] normals = new Vector3[vertices.Count];
            foreach (var face in faces) {
                if (face.Length < 3) continue;
                Vector3 v0 = new Vector3(vertices[face[0].V].X, vertices[face[0].V].Y, vertices[face[0].V].Z);
                Vector3 v1 = new Vector3(vertices[face[1].V].X, vertices[face[1].V].Y, vertices[face[1].V].Z);
                Vector3 v2 = new Vector3(vertices[face[2].V].X, vertices[face[2].V].Y, vertices[face[2].V].Z);
                
                Vector3 edge1 = Vector3.Subtract(v1, v0);
                Vector3 edge2 = Vector3.Subtract(v2, v0);
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);

                for (int i = 0; i < face.Length; i++) {
                    normals[face[i].V] = Vector3.Add(normals[face[i].V], faceNormal);
                }
            }

            vertexNormals = new List<Vector3>(vertices.Count);
            for (int i = 0; i < normals.Length; i++) vertexNormals.Add(normals[i].Normalize());
        }

        // Трансформация вершины "на лету" с подготовкой к перспективной коррекции
        private VertexData TransformVertex(FaceVertex fv, Matrix4x4 matWorld, Matrix4x4 transform)
        {
            Vector4 wPos = Matrix4x4.Multiply(matWorld, vertices[fv.V]);
            Vector4 sPos = Matrix4x4.Multiply(transform, vertices[fv.V]);
            
            float w = sPos.W;
            if (w != 0) { sPos.X /= w; sPos.Y /= w; sPos.Z /= w; }
            
            Vector4 nPos = Matrix4x4.Multiply(matWorld, new Vector4(vertexNormals[fv.V].X, vertexNormals[fv.V].Y, vertexNormals[fv.V].Z, 0));
            Vector2 uv = fv.Vt >= 0 && fv.Vt < uvs.Count ? uvs[fv.Vt] : new Vector2(0, 0);

            // Подготовка перспективной коррекции: 1/Z, U/Z, V/Z
            float z_inv = w == 0 ? 0.00001f : 1.0f / w;

            return new VertexData {
                ScreenPos = sPos,
                WorldPos = new Vector3(wPos.X, wPos.Y, wPos.Z),
                Normal = new Vector3(nPos.X, nPos.Y, nPos.Z).Normalize(),
                U_over_Z = uv.X * z_inv,
                V_over_Z = uv.Y * z_inv,
                Z_inv = z_inv
            };
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

            BitmapData data = renderTarget.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, renderTarget.PixelFormat);
            unsafe {
                int* ptr = (int*)data.Scan0;
                int bg = unchecked((int)0xFF121212);
                
                Parallel.For(0, h, y => {
                    int* row = ptr + (y * w);
                    int zOffset = y * w;
                    for (int x = 0; x < w; x++) { row[x] = bg; zBuffer[zOffset + x] = float.MaxValue; }
                });

                Parallel.ForEach(faces, face => {
                    if (face.Length < 3) return;

                    VertexData[] projFace = new VertexData[face.Length];
                    for(int i = 0; i < face.Length; i++) projFace[i] = TransformVertex(face[i], matWorld, transform);

                    Vector3 edge1 = Vector3.Subtract(projFace[1].WorldPos, projFace[0].WorldPos);
                    Vector3 edge2 = Vector3.Subtract(projFace[2].WorldPos, projFace[0].WorldPos);
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2).Normalize();
                    Vector3 viewDir = Vector3.Subtract(cameraPos, projFace[0].WorldPos).Normalize();

                    if (Vector3.Dot(faceNormal, viewDir) >= 0) {
                        for (int i = 1; i < face.Length - 1; i++) {
                            FillTrianglePhong(projFace[0], projFace[i], projFace[i + 1], ptr, w, h, cameraPos, matWorld);
                        }
                    }
                });
            }
            renderTarget.UnlockBits(data);
            renderBox.Image = renderTarget;
        }

        private unsafe void FillTrianglePhong(VertexData p0, VertexData p1, VertexData p2, int* ptr, int w, int h, Vector3 cameraPos, Matrix4x4 matWorld)
        {
            if (p0.ScreenPos.Y > p1.ScreenPos.Y) Swap(ref p0, ref p1);
            if (p0.ScreenPos.Y > p2.ScreenPos.Y) Swap(ref p0, ref p2);
            if (p1.ScreenPos.Y > p2.ScreenPos.Y) Swap(ref p1, ref p2);

            float total_height = p2.ScreenPos.Y - p0.ScreenPos.Y;
            if (total_height < 1e-5) return; 

            int y_start = Math.Max(0, (int)Math.Ceiling(p0.ScreenPos.Y));
            int y_end   = Math.Min(h - 1, (int)Math.Ceiling(p2.ScreenPos.Y));

            for (int y = y_start; y < y_end; y++) {
                bool second_half = y > p1.ScreenPos.Y || Math.Abs(p1.ScreenPos.Y - p0.ScreenPos.Y) < 1e-5;
                float segment_height = second_half ? (p2.ScreenPos.Y - p1.ScreenPos.Y) : (p1.ScreenPos.Y - p0.ScreenPos.Y);
                if (segment_height < 1e-5) continue;

                float alpha = (y - p0.ScreenPos.Y) / total_height;
                float beta  = (y - (second_half ? p1.ScreenPos.Y : p0.ScreenPos.Y)) / segment_height;

                VertexData A = VertexData.Lerp(p0, p2, alpha);
                VertexData B = second_half ? VertexData.Lerp(p1, p2, beta) : VertexData.Lerp(p0, p1, beta);

                if (A.ScreenPos.X > B.ScreenPos.X) Swap(ref A, ref B);

                int x_left = Math.Max(0, (int)Math.Ceiling(A.ScreenPos.X));
                int x_right = Math.Min(w - 1, (int)Math.Ceiling(B.ScreenPos.X));

                for (int x = x_left; x < x_right; x++) {
                    float phi = (B.ScreenPos.X - A.ScreenPos.X) < 1e-5 ? 1.0f : (x - A.ScreenPos.X) / (B.ScreenPos.X - A.ScreenPos.X);
                    
                    float z = A.ScreenPos.Z + (B.ScreenPos.Z - A.ScreenPos.Z) * phi;
                    int idx = y * w + x;
                    
                    if (z < zBuffer[idx] - 0.0001f) {
                        zBuffer[idx] = z;

                        // ПЕРСПЕКТИВНАЯ КОРРЕКЦИЯ ТЕКСТУР: восстанавливаем истинные U и V
                        float z_inv = A.Z_inv + (B.Z_inv - A.Z_inv) * phi;
                        float perspZ = 1.0f / z_inv;
                        float u = (A.U_over_Z + (B.U_over_Z - A.U_over_Z) * phi) * perspZ;
                        float v = (A.V_over_Z + (B.V_over_Z - A.V_over_Z) * phi) * perspZ;

                        Vector3 worldP = Vector3.Lerp(A.WorldPos, B.WorldPos, phi);
                        Vector3 normalP = Vector3.Lerp(A.Normal, B.Normal, phi).Normalize();

                        ptr[idx] = CalculatePhongLight(worldP, normalP, u, v, matWorld, cameraPos);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CalculatePhongLight(Vector3 worldPos, Vector3 normal, float u, float v, Matrix4x4 matWorld, Vector3 cameraPos)
        {
            Vector3 viewDir = Vector3.Subtract(cameraPos, worldPos).Normalize();
            
            // Если загружена КАРТА НОРМАЛЕЙ (Модельное пространство)
            if (texNormal != null) {
                Vector3 modelNorm = texNormal.SampleNormal(u, v);
                Vector4 nWorld = Matrix4x4.Multiply(matWorld, new Vector4(modelNorm.X, modelNorm.Y, modelNorm.Z, 0));
                normal = new Vector3(nWorld.X, nWorld.Y, nWorld.Z).Normalize();
            }

            // ДИФФУЗНАЯ КАРТА
            Color diffColor = texDiffuse != null ? texDiffuse.Sample(u, v) : objectColor;
            
            // ЗЕРКАЛЬНАЯ КАРТА
            float localKs = texSpecular != null ? texSpecular.Sample(u, v).R / 255.0f : Ks;

            float ambient = Ka;
            float diff = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
            float diffuse = Kd * diff;

            float specular = 0.0f;
            if (diff > 0.0f) {
                Vector3 reflectDir = Vector3.Subtract(Vector3.Multiply(normal, 2.0f * Vector3.Dot(normal, lightDir)), lightDir).Normalize();
                float spec = (float)Math.Pow(Math.Max(Vector3.Dot(viewDir, reflectDir), 0.0f), shininess);
                specular = localKs * spec;
            }

            float intensity = Math.Min(ambient + diffuse, 1.0f);

            int r = Math.Min((int)(diffColor.R * intensity) + (int)(255 * specular), 255);
            int g = Math.Min((int)(diffColor.G * intensity) + (int)(255 * specular), 255);
            int b = Math.Min((int)(diffColor.B * intensity) + (int)(255 * specular), 255);

            return unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
        }

        private void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }
    }

    #region Helpers & Math
    // Сверхбыстрый класс для чтения пикселей напрямую из памяти
    public class Texture {
        public int[] Data;
        public int Width, Height;

        public Texture(string path) {
            using var bmp = new Bitmap(path);
            Width = bmp.Width; Height = bmp.Height;
            Data = new int[Width * Height];
            var bd = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bd.Scan0, Data, 0, Data.Length);
            bmp.UnlockBits(bd);
        }

        public Color Sample(float u, float v) {
            if (Data == null) return Color.White;
            
            // Повторение текстуры (Wrap/Repeat)
            u = u - (float)Math.Floor(u);
            v = v - (float)Math.Floor(v);
            
            int x = (int)(u * (Width - 1));
            int y = (int)((1f - v) * (Height - 1)); // Переворот V для стандарта OBJ
            
            x = Math.Max(0, Math.Min(Width - 1, x));
            y = Math.Max(0, Math.Min(Height - 1, y));
            
            return Color.FromArgb(Data[y * Width + x]);
        }

        public Vector3 SampleNormal(float u, float v) {
            Color c = Sample(u, v);
            // Преобразование цвета [0, 1] в нормаль [-1, 1] по формуле из лабы
            return new Vector3((c.R / 255f) * 2f - 1f, (c.G / 255f) * 2f - 1f, (c.B / 255f) * 2f - 1f).Normalize();
        }
    }

    public struct FaceVertex { public int V, Vt, Vn; }

    public struct VertexData {
        public Vector4 ScreenPos;
        public Vector3 WorldPos;
        public Vector3 Normal;
        public float U_over_Z, V_over_Z, Z_inv; // Для перспективной коррекции

        public static VertexData Lerp(VertexData a, VertexData b, float t) {
            return new VertexData {
                ScreenPos = Vector4.Lerp(a.ScreenPos, b.ScreenPos, t),
                WorldPos = Vector3.Lerp(a.WorldPos, b.WorldPos, t),
                Normal = Vector3.Lerp(a.Normal, b.Normal, t),
                U_over_Z = a.U_over_Z + (b.U_over_Z - a.U_over_Z) * t,
                V_over_Z = a.V_over_Z + (b.V_over_Z - a.V_over_Z) * t,
                Z_inv = a.Z_inv + (b.Z_inv - a.Z_inv) * t
            };
        }
    }

    public struct Vector2 {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
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