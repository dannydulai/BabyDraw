using System;
using System.Collections.Generic;

using OpenTK;
using OpenTK.Graphics.ES20;
using OpenTK.Platform.iPhoneOS;

using MonoTouch.Foundation;
using MonoTouch.CoreAnimation;
using MonoTouch.ObjCRuntime;
using MonoTouch.OpenGLES;
using MonoTouch.UIKit;

namespace BabyDraw
{
    [Register("EAGLView")]
    public class EAGLView : iPhoneOSGameView
    {
        [Export("initWithCoder:")]
        public EAGLView(NSCoder coder) : base(coder)
        {
            LayerRetainsBacking = true;
            LayerColorFormat = EAGLColorFormat.RGBA8;
        }

        [Export("layerClass")]
        public static new Class GetLayerClass()
        {
            return iPhoneOSGameView.GetLayerClass();
        }

        protected override void ConfigureLayer(CAEAGLLayer eaglLayer)
        {
            eaglLayer.Opaque = true;
        }

        protected override void CreateFrameBuffer()
        {
            ContextRenderingApi = EAGLRenderingAPI.OpenGLES2;
            base.CreateFrameBuffer();
            LoadShaders();
            SetupGl();
        }

        protected override void DestroyFrameBuffer()
        {
            base.DestroyFrameBuffer();
            DestroyShaders();
        }

        void DestroyShaders()
        {
            if (_program != 0) { GL.DeleteProgram(_program); _program = 0; }
        }

        #region DisplayLink support

        int frameInterval;
        CADisplayLink displayLink;

        public bool IsAnimating { get; private set; }

        // How many display frames must pass between each time the display link fires.
        public int FrameInterval {
            get {
                return frameInterval;
            }
            set {
                if (value <= 0)
                    throw new ArgumentException();
                frameInterval = value;
                if (IsAnimating) {
                    StopAnimating();
                    StartAnimating();
                }
            }
        }

        public void StartAnimating()
        {
            if (IsAnimating)
                return;

            CreateFrameBuffer();
            CADisplayLink displayLink = UIScreen.MainScreen.CreateDisplayLink(this, new Selector("drawFrame"));
            displayLink.FrameInterval = frameInterval;
            displayLink.AddToRunLoop(NSRunLoop.Current, NSRunLoop.NSDefaultRunLoopMode);
            this.displayLink = displayLink;

            IsAnimating = true;
        }

        public void StopAnimating()
        {
            if (!IsAnimating)
                return;
            displayLink.Invalidate();
            displayLink = null;
            DestroyFrameBuffer();
            IsAnimating = false;
        }

        [Export("drawFrame")]
        void DrawFrame()
        {
            OnRenderFrame(new FrameEventArgs());
        }

        #endregion


        #region Shader utilities

        static bool CompileShader(All type, string file, out int shader)
        {
            string src = System.IO.File.ReadAllText(file);
            shader = GL.CreateShader(type);
            GL.ShaderSource(shader, 1, new string[] { src },(int[])null);
            GL.CompileShader(shader);

#if DEBUG
            int logLength = 0;
            GL.GetShader(shader, All.InfoLogLength, ref logLength);
            if (logLength > 0) {
                var infoLog = new System.Text.StringBuilder();
                GL.GetShaderInfoLog(shader, logLength, ref logLength, infoLog);
                Console.WriteLine("Shader compile log:\n{0}", infoLog);
            }
#endif
            int status = 0;
            GL.GetShader(shader, All.CompileStatus, ref status);
            if (status == 0) { GL.DeleteShader(shader); return false; }

            return true;
        }

        static bool LinkProgram(int prog)
        {
            GL.LinkProgram(prog);

#if DEBUG
            int logLength = 0;
            GL.GetProgram(prog, All.InfoLogLength, ref logLength);
            if (logLength > 0) {
                var infoLog = new System.Text.StringBuilder();
                GL.GetProgramInfoLog(prog, logLength, ref logLength, infoLog);
                Console.WriteLine("Program link log:\n{0}", infoLog);
            }
#endif
            int status = 0;
            GL.GetProgram(prog, All.LinkStatus, ref status);
            if (status == 0)
                return false;

            return true;
        }

        static bool ValidateProgram(int prog)
        {
            GL.ValidateProgram(prog);

            int logLength = 0;
            GL.GetProgram(prog, All.InfoLogLength, ref logLength);
            if (logLength > 0) {
                var infoLog = new System.Text.StringBuilder();
                GL.GetProgramInfoLog(prog, logLength, ref logLength, infoLog);
                Console.WriteLine("Program validate log:\n{0}", infoLog);
            }

            int status = 0;
            GL.GetProgram(prog, All.LinkStatus, ref status);
            if (status == 0)
                return false;

            return true;
        }

        #endregion

        bool LoadShaders()
        {
            int vertShader, fragShader;

            _program = GL.CreateProgram();

            var vertShaderPathname = NSBundle.MainBundle.PathForResource("Shader", "vsh");
            if (!CompileShader(All.VertexShader, vertShaderPathname, out vertShader)) {
                Console.WriteLine("Failed to compile vertex shader");
                return false;
            }

            var fragShaderPathname = NSBundle.MainBundle.PathForResource("Shader", "fsh");
            if (!CompileShader(All.FragmentShader, fragShaderPathname, out fragShader)) {
                Console.WriteLine("Failed to compile fragment shader");
                return false;
            }

            GL.AttachShader(_program, vertShader);
            GL.AttachShader(_program, fragShader);
            GL.BindAttribLocation(_program, ATTRIB_VERTEX, "inPos");
            GL.BindAttribLocation(_program, ATTRIB_TEXTUREPOSITON, "inTexCoord");


            if (!LinkProgram(_program)) {
                Console.WriteLine("Failed to link program: {0:x}", _program);
                if (vertShader != 0) GL.DeleteShader(vertShader);
                if (fragShader != 0) GL.DeleteShader(fragShader);
                if (_program != 0) { GL.DeleteProgram(_program); _program = 0; }
                return false;
            }

            if (vertShader != 0) { GL.DetachShader(_program, vertShader); GL.DeleteShader(vertShader); }
            if (fragShader != 0) { GL.DetachShader(_program, fragShader); GL.DeleteShader(fragShader); }

            return true;
        }

        float[] squareVertices  = { -1.0f, 1.0f, 1.0f, 1.0f, -1.0f, -1.0f, 1.0f, -1.0f };
        float[] textureVertices = {  0.0f, 0.0f, 1.0f, 0.0f,  0.0f,  1.0f, 1.0f,  1.0f };

        const int ATTRIB_VERTEX = 0;
        const int ATTRIB_TEXTUREPOSITON = 1;
        int _program;
        int _texture = -1;

        All err;

        void SetupGl()
        {
            // Use shader program.
            GL.UseProgram(_program);
            err = GL.GetError();
            if (err != All.False) throw new Exception("ERROR: UseProgram(" + _program + ") ERROR: = " + err);

            int txsampler = GL.GetUniformLocation(_program, "tex0");
            GL.Uniform1(txsampler, (int)0);

            GL.Disable(All.CullFace);
            GL.Disable(All.DepthTest);

            // we only have one texture, so activate it now
            GL.ActiveTexture(All.Texture0);
            err = GL.GetError();
            if (err != All.False) throw new Exception("ERROR: ActiveTexture(0) ERROR: = " + err);

            //

            GL.VertexAttribPointer(ATTRIB_VERTEX, 2, All.Float, false, 0, squareVertices);
            GL.EnableVertexAttribArray(ATTRIB_VERTEX);
            GL.VertexAttribPointer(ATTRIB_TEXTUREPOSITON, 2, All.Float, false, 0, textureVertices);
            GL.EnableVertexAttribArray(ATTRIB_TEXTUREPOSITON);

            //

            _flavors = new Pixels[] {
                new Solids((int)UIScreen.MainScreen.Bounds.Width/3,
                           (int)UIScreen.MainScreen.Bounds.Height/3),
                new Fire((int)UIScreen.MainScreen.Bounds.Width/3,
                         (int)UIScreen.MainScreen.Bounds.Height/3),
                new Ice((int)UIScreen.MainScreen.Bounds.Width/3,
                         (int)UIScreen.MainScreen.Bounds.Height/3)
            };
        }

        Pixels[] _flavors;
        int _flavor;
        Pixels _current { get { return _flavors[_flavor]; } }
        int _corner1, _corner2;

        // this keeps track of current touches so when we go from 0->1, we know it's
        // a new draw. Some Pixel classes want to know this info.
        List<IntPtr> _touches = new List<IntPtr>();

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            base.TouchesBegan(touches, evt);
            foreach (UITouch touch in evt.AllTouches.ToArray<UITouch>()) {

                // update _touches so we can do StartDraw()
                if (_touches.Count == 0) _current.StartDraw();
                if (touch.Phase == UITouchPhase.Began)
	                _touches.Add(touch.Handle);
                Console.WriteLine("began: handle {0}, phase {1}", touch.Handle, touch.Phase);

                // first check the upper left and upper right touch areas for
                // touches. if so, do special actions, otherwise draw
                int x = (int)touch.LocationInView(this).X;
                int y = (int)touch.LocationInView(this).Y;

                if (x < 100 && y < 100) {
                    _corner1++;
                    if (_corner1 >= 3) {
                        _current.Clear();
                        _corner1 = 0;
                        return;
                    }
                } else if (x > (int)UIScreen.MainScreen.Bounds.Width - 100 && y < 100) {
                    _corner2++;
                    if (_corner2 >= 3) {
                        _current.Clear();
                        _flavor = (_flavor + 1) % _flavors.Length;
                        _corner2 = 0;
                        return;
                    }
                } else {
                    _corner1 = 0;
                    _corner2 = 0;
                }

                _current.DrawPoint(x, y);
            }
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            base.TouchesMoved(touches, evt);
            foreach (UITouch touch in evt.AllTouches.ToArray<UITouch>()) {
//                Console.WriteLine("moved: handle {0}", touch.Handle);
                // draw muliptle point line between last point of touch and
                // current point of touch. we vary the number of steps roughly
                // so we arent drawing so many overlapping circles
                double x = (double)touch.PreviousLocationInView(this).X;
                double y = (double)touch.PreviousLocationInView(this).Y;

                double diffx = (double)touch.LocationInView(this).X - x;
                double diffy = (double)touch.LocationInView(this).Y - y;

//                double absdiffx = Math.Abs(diffx);
//                double absdiffy = Math.Abs(diffy);
//
//                int steps;
//                if (Math.Max(absdiffx, absdiffy) < 10) {
//                    steps = 2;
//                } else if (Math.Max(absdiffx, absdiffy) < 50) {
//                    steps = 5;
//                } else if (Math.Max(absdiffx, absdiffy) < 150) {
//                    steps = 10;
//                } else {
//                    steps = 20;
//                }
//
//                diffx /= steps;
//                diffy /= steps;
//
//                int i = 0;
//                while (i++ < steps) {
//                    x += diffx;
//                    y += diffy;
//                    _current.DrawPoint((int)x, (int)y);
//                }

                _current.DrawPoint((int)(x + diffx), (int)(y + diffy));
            }
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            base.TouchesEnded(touches, evt);

            // update _touches so we can do StartDraw()
            foreach (UITouch touch in evt.AllTouches.ToArray<UITouch>()) {
                if (touch.Phase == UITouchPhase.Ended || touch.Phase == UITouchPhase.Cancelled)
                    _touches.Remove(touch.Handle);
                Console.WriteLine("ended: handle {0}, phase {1}", touch.Handle, touch.Phase);
            }
        }

        public override void TouchesCancelled (NSSet touches, UIEvent evt)
        {
            base.TouchesCancelled (touches, evt);

            // update _touches so we can do StartDraw()
            foreach (UITouch touch in evt.AllTouches.ToArray<UITouch>()) {
                if (touch.Phase == UITouchPhase.Ended || touch.Phase == UITouchPhase.Cancelled)
                    _touches.Remove(touch.Handle);
                Console.WriteLine("canceled: handle {0}, phase {1}", touch.Handle, touch.Phase);
            }
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            if (_current.IfDirtyDoClean()) {

                base.OnRenderFrame(e);
                MakeCurrent();

                _current.UpdateTexture(ref _texture);

                // draw!
                GL.DrawArrays(All.TriangleStrip, 0, 4);
                SwapBuffers();
            }
        }
    }

    public abstract class Pixels
    {
        protected bool   _dirty = true;
        protected byte[] _pixels; // holds the texture data
        protected int    _w;
        protected int    _h;

        // w/h is screen size
        public Pixels(int w, int h) {
            _w = w;
            _h = h;
            _pixels = new byte[(_w * 4) * _h];
        }

        // in case you need to know when we go from 0->N touches
        public abstract void StartDraw();

        // draw 1 point -- probably a big circle
        public void DrawPoint(int x, int y) { _DrawPoint(x/3, y/3); } 
        protected abstract void _DrawPoint(int x, int y);

        // kill the display
        public virtual void Clear() {
            _dirty = true;
            _ClearPixels();
        }
        protected virtual void _ClearPixels() {
            int i = 0;
            while (i < _w * _h * 4) {
                _pixels[i+0] = 0;
                _pixels[i+1] = 0;
                _pixels[i+2] = 0;
                _pixels[i+3] = 255;
                i += 4;
            }
        }

        // converts the pixels array into an opengl texture
        public void UpdateTexture(ref int texture) {
            All err;

            if (texture == -1) {
                GL.GenTextures(1, ref texture);
                err = GL.GetError();
                if (err != All.False) throw new Exception("ERROR: 'GL.GenTextures()' -> err=" + err);

                GL.BindTexture(All.Texture2D, texture);
                err = GL.GetError();
                if (err != All.False) throw new Exception("ERROR: 'GL.BindTexture()' -> err=" + err);

                GL.TexParameter(All.Texture2D, All.TextureMinFilter, (int)All.Linear);
                GL.TexParameter(All.Texture2D, All.TextureMagFilter, (int)All.Linear);
                GL.TexParameter(All.Texture2D, All.TextureWrapS, (int)All.ClampToEdge);
                GL.TexParameter(All.Texture2D, All.TextureWrapT, (int)All.ClampToEdge);
            }

            unsafe
            {
                fixed (byte* _pRGBAs = _pixels)
                {
                    GL.TexImage2D(All.Texture2D, (int)0, (int)All.Rgba, _w, _h, 0, All.Rgba, All.UnsignedByte, new IntPtr(_pRGBAs));
                    err = GL.GetError();
                    if (err != All.False) throw new Exception("ERROR: 'GL.TexImage2D()' -> err=" + err);
                }
            }
        }

        // dirty flag -- don't need to render if this isnt true
        public bool IfDirtyDoClean() {
            bool ret = _dirty;
            _dirty = false;
            return ret;
        }
    }

    public class Gradient : Pixels
    {
        protected double[] _idxes; // holds the index values
        protected Color[] _pal; // holds the palette

        protected struct Color {
            public int R, G, B;
        }

        public Gradient(int w, int h) : base(w, h) {
            _idxes = new double[_w * _h];
        }

        public override void StartDraw() { }

        public override void Clear() {
            base.Clear();
            int i = 0;
            while (i < _w * _h)
                _idxes[i++] = 0;
        }

        protected override void _DrawPoint(int x, int y) {
            _dirty = true;
            const int R = 10;

            x -= R;
            y -= R;

            int k = 0;
            while (k < R*2) {
                int j = 0;
                while (j < R*2) {
                    if (j + x < _w && k + y < _h &&
                        j + x >= 0 && k + y >= 0)
                    {
                        int idx = (x + j) + ((y + k) * _w);

                        double radius = R;
                        double xx = (double)(j - radius);
                        double yy = (double)(k - radius);
                        double r = Math.Sqrt(xx*xx + yy*yy);

                        if (r <= radius) {
                            r = (double)radius - r;
                            r /= (double)radius;

                            _idxes[idx] = (_idxes[idx] + (r * 22)) % 256.0;

                            Color c = _pal[(byte)Math.Floor(_idxes[idx])];
                            int pos = idx*4;
                            _pixels[pos+0] = (byte)c.R;
                            _pixels[pos+1] = (byte)c.G;
                            _pixels[pos+2] = (byte)c.B;
                        }
                    }
                    j++;
                }
                k++;
            }
        }
    }

    public class Fire : Gradient
    {
        public Fire(int w, int h) : base(w, h)
        {
            _pal = new Color[256];

            int i;
            int idx = 0;
            for (i=0; i < 64; i++) {
                _pal[idx].R = i << 2;
                _pal[idx].G = 0;
                _pal[idx].B = 0;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = 255;
                _pal[idx].G = i << 2;
                _pal[idx].B = 0;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = 255;
                _pal[idx].G = 255;
                _pal[idx].B = i << 2;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = 255 - (i << 2);
                _pal[idx].G = 255 - (i << 2);
                _pal[idx].B = 255 - (i << 2);
                idx++;
            }
        }
    }

    public class Ice : Gradient
    {
        public Ice(int w, int h) : base(w, h)
        {
            _pal = new Color[256];

            int i;
            int idx = 0;
            for (i=0; i < 64; i++) {
                _pal[idx].R = 0;
                _pal[idx].G = 0;
                _pal[idx].B = i << 2;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = 0;
                _pal[idx].B = 255;
                _pal[idx].G = i << 2;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = i << 2;
                _pal[idx].G = 255;
                _pal[idx].B = 255;
                idx++;
            }
            for (i=0; i < 64; i++) {
                _pal[idx].R = 255 - (i << 2);
                _pal[idx].G = 255 - (i << 2);
                _pal[idx].B = 255 - (i << 2);
                idx++;
            }
        }
    }

    public class Solids : Pixels
    {
        // this class draws solid circles, but varys the color every time you
        // draw, and varys the bg every time you clear. bgs are dark, fgs are light
        Random _rand = new Random();
        byte _r;
        byte _g;
        byte _b;

        public Solids(int w, int h) : base(w, h)
        {
            _ClearPixels();
        }

        public override void StartDraw() {
            switch (_rand.Next(7)) {
                case 0:
                    _r = (byte)(_rand.Next(64) + 192);
                    _g = (byte)(_rand.Next(64) + 192);
                    _b = (byte)(_rand.Next(64) + 192);
                    break;
                case 1:
                    _r = (byte)(_rand.Next(64) + 192);
                    _g = (byte)(_rand.Next(64) + 192);
                    _b = (byte)(_rand.Next(64));
                    break;
                case 2:
                    _r = (byte)(_rand.Next(64) + 192);
                    _g = (byte)(_rand.Next(64));
                    _b = (byte)(_rand.Next(64) + 192);
                    break;
                case 3:
                    _r = (byte)(_rand.Next(64));
                    _g = (byte)(_rand.Next(64) + 192);
                    _b = (byte)(_rand.Next(64) + 192);
                    break;
                case 4:
                    _r = (byte)(_rand.Next(64));
                    _g = (byte)(_rand.Next(64) + 192);
                    _b = (byte)(_rand.Next(64));
                    break;
                case 5:
                    _r = (byte)(_rand.Next(64) + 192);
                    _g = (byte)(_rand.Next(64));
                    _b = (byte)(_rand.Next(64));
                    break;
                case 6:
                    _r = (byte)(_rand.Next(64));
                    _g = (byte)(_rand.Next(64));
                    _b = (byte)(_rand.Next(64) + 192);
                    break;
            }
        }

        protected override void _ClearPixels() {
            byte r = (byte)(_rand.Next(128));
            byte g = (byte)(_rand.Next(128));
            byte b = (byte)(_rand.Next(128));

            int i = 0;
            while (i < _w * _h * 4) {
                _pixels[i+0] = r;
                _pixels[i+1] = g;
                _pixels[i+2] = b;
                _pixels[i+3] = 255;
                i += 4;
            }
        }

        protected override void _DrawPoint(int x, int y) {
            _dirty = true;
            const int R = 10;

            x -= R;
            y -= R;

            int k = 0;
            while (k < R*2) {
                int j = 0;
                while (j < R*2) {
                    if (j + x < _w && k + y < _h &&
                        j + x >= 0 && k + y >= 0)
                    {
                        int idx = (x + j) + ((y + k) * _w);

                        double radius = R;
                        double xx = (double)(j - radius);
                        double yy = (double)(k - radius);
                        double r = Math.Sqrt(xx*xx + yy*yy);

                        if (r <= radius) {
                            _pixels[(idx*4)+0] = _r;
                            _pixels[(idx*4)+1] = _g;
                            _pixels[(idx*4)+2] = _b;
                        }
                    }
                    j++;
                }
                k++;
            }
        }
    }
}
