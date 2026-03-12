using System.Drawing.Imaging;
using System.Globalization;

namespace ModelViewer
{
    public partial class Form1 : Form
    {
        private PictureBox renderBox = null!;
        private Button btnLoad = null!;
        private Bitmap renderTarget = null!;
        
        private List<Vector4> vertices = new List<Vector4>();
        private List<int[]> faces = new List<int[]>();
        private Vector4[] projectedBuffer = Array.Empty<Vector4>();

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
            this.Text = "Model Viewer";
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
                
                Parallel.For(0, h, y => {
                    int* row = ptr + (y * w);
                    for (int x = 0; x < w; x++) row[x] = bg;
                });

                int lineColor = Color.Lime.ToArgb();

                Parallel.ForEach(faces, face => {
                    if (face.Length < 3) return;

                    Vector4 v0w = Matrix4x4.Multiply(matWorld, vertices[face[0]]);
                    Vector4 v1w = Matrix4x4.Multiply(matWorld, vertices[face[1]]);
                    Vector4 v2w = Matrix4x4.Multiply(matWorld, vertices[face[2]]);

                    Vector3 edge1 = new Vector3(v1w.X - v0w.X, v1w.Y - v0w.Y, v1w.Z - v0w.Z);
                    Vector3 edge2 = new Vector3(v2w.X - v0w.X, v2w.Y - v0w.Y, v2w.Z - v0w.Z);
                    Vector3 normal = Vector3.Cross(edge1, edge2).Normalize();
                    Vector3 viewDir = new Vector3(v0w.X - cameraPos.X, v0w.Y - cameraPos.Y, v0w.Z - cameraPos.Z).Normalize();

                    if (Vector3.Dot(normal, viewDir) < 0) {
                        for (int i = 0; i < face.Length; i++) {
                            var p1 = projectedBuffer[face[i]];
                            var p2 = projectedBuffer[face[(i + 1) % face.Length]];
                            
                            if (IsVisible(p1, p2, w, h)) 
                                DrawLineFast(p1, p2, lineColor, ptr, w, h);
                        }
                    }
                });
            }
            renderTarget.UnlockBits(data);
            renderBox.Image = renderTarget;
        }

        private bool IsVisible(Vector4 p1, Vector4 p2, int w, int h) {
            if (p1.W <= 0 || p2.W <= 0) return false;
            if ((p1.X < 0 && p2.X < 0) || (p1.X > w && p2.X > w)) return false;
            if ((p1.Y < 0 && p2.Y < 0) || (p1.Y > h && p2.Y > h)) return false;
            return true;
        }

        private unsafe void DrawLineFast(Vector4 v1, Vector4 v2, int color, int* ptr, int w, int h)
        {
            int x0 = (int)v1.X, y0 = (int)v1.Y, x1 = (int)v2.X, y1 = (int)v2.Y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = (dx > dy ? dx : -dy) / 2, e2;
            while (true) {
                if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h) ptr[y0 * w + x0] = color;
                if (x0 == x1 && y0 == y1) break;
                e2 = err;
                if (e2 > -dx) { err -= dy; x0 += sx; }
                if (e2 < dy) { err += dx; y0 += sy; }
            }
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
    public struct Vector4 { public float X, Y, Z, W; public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; } }
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