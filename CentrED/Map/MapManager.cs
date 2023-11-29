using CentrED.Client;
using CentrED.Network;
using CentrED.Renderer;
using CentrED.Renderer.Effects;
using CentrED.Tools;
using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static CentrED.Application;

namespace CentrED.Map;

public class MapManager
{
    private readonly GraphicsDevice _gfxDevice;

    private MapEffect _mapEffect;
    public MapEffect MapEffect => _mapEffect;
    private readonly MapRenderer _mapRenderer;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _background;

    private RenderTarget2D _shadowsBuffer;
    private RenderTarget2D _selectionBuffer;

    public Tool? ActiveTool;

    private readonly PostProcessRenderer _postProcessRenderer;

    public CentrEDClient Client;

    public bool ShowLand = true;
    public bool ShowStatics = true;
    public bool ShowShadows = true;
    public bool ShowVirtualLayer = false;
    public int VirtualLayerZ = 0;
    public Vector3 VirtualLayerTilePos = Vector3.Zero;

    public readonly Camera Camera = new();
    private Camera _lightSourceCamera = new();

    private LightingState _lightingState = new();
    private DepthStencilState _depthStencilState = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = true,
        DepthBufferFunction = CompareFunction.Less,
        StencilEnable = false
    };

    public readonly float TILE_SIZE = 31.11f;

    public int minZ = -127;
    public int maxZ = 127;

    public bool StaticFilterEnabled;
    public bool StaticFilterInclusive = true;
    public SortedSet<int> StaticFilterIds = new();

    public int[] ValidLandIds { get; private set; }
    public int[] ValidStaticIds { get; private set; }

    private void DarkenTexture(ushort[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ushort c = pixels[i];

            int red = (c >> 10) & 0x1F;
            int green = (c >> 5) & 0x1F;
            int blue = c & 0x1F;

            red = (int)(red * 0.85355339f);
            green = (int)(green * 0.85355339f);
            blue = (int)(blue * 0.85355339f);

            pixels[i] = (ushort)((1 << 15) | (red << 10) | (green << 5) | blue);
        }
    }

    public MapManager(GraphicsDevice gd)
    {
        _gfxDevice = gd;
        _mapEffect = new MapEffect(gd);
        _mapRenderer = new MapRenderer(gd);
        _shadowsBuffer = new RenderTarget2D
        (
            gd,
            gd.PresentationParameters.BackBufferWidth * 2,
            gd.PresentationParameters.BackBufferHeight * 2,
            false,
            SurfaceFormat.Single,
            DepthFormat.Depth24
        );

        _selectionBuffer = new RenderTarget2D
        (
            gd,
            gd.PresentationParameters.BackBufferWidth,
            gd.PresentationParameters.BackBufferHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24
        );
        _postProcessRenderer = new PostProcessRenderer(gd);
        _spriteBatch = new SpriteBatch(gd);
        _background = CEDGame.Content.Load<Texture2D>("background");

        Client = CEDClient;
        Client.LandTileReplaced += (tile, newId) => { LandTiles.Find(l => l.LandTile.Equals(tile))?.UpdateId(newId); };
        Client.LandTileElevated += (tile, newZ) =>
        {
            Vector2[] offsets =
            {
                new(0, 0),  //top
                new(-1, 0), //right
                new(0, -1), //left
                new(-1, -1) //bottom
            };

            for (var i = 0; i < offsets.Length; i++)
            {
                var offset = offsets[i];
                var landObject = LandTiles.Find
                    (l => l.LandTile.X == (ushort)(tile.X + offset.X) && l.LandTile.Y == (ushort)(tile.Y + offset.Y));
                if (landObject != null)
                {
                    landObject.Vertices[i].Position.Z = newZ * MapObject.TILE_Z_SCALE;
                    landObject.UpdateId(landObject.LandTile.Id); //Just refresh ID to refresh if it's flat
                }
            }
            //We need to update normals too
        };
        Client.BlockLoaded += block => { block.StaticBlock.SortTiles(ref TileDataLoader.Instance.StaticData); };
        Client.BlockUnloaded += block =>
        {
            var landTiles = block.LandBlock.Tiles;
            foreach (var landTile in landTiles)
            {
                LandTiles.RemoveAll(lo => lo.LandTile.Equals(landTile));
            }
            var staticTiles = block.StaticBlock.AllTiles();
            foreach (var staticTile in staticTiles)
            {
                StaticTiles.RemoveAll(so => so.StaticTile.Equals(staticTile));
            }
        };
        Client.StaticTileRemoved += tile => { StaticTiles.RemoveAll(so => so.StaticTile.Equals(tile)); };
        Client.StaticTileAdded += tile =>
        {
            tile.Block?.SortTiles(ref TileDataLoader.Instance.StaticData);
            StaticTiles.Add(new StaticObject(tile));
        };
        Client.StaticTileMoved += (tile, newX, newY) =>
        {
            StaticTiles.RemoveAll(so => so.StaticTile.Equals(tile));
            var newTile = new StaticTile(tile.Id, newX, newY, tile.Z, tile.Hue, tile.Block);
            StaticTiles.Add(new StaticObject(newTile));
        };
        Client.StaticTileElevated += (tile, newZ) =>
        {
            StaticTiles.RemoveAll(so => so.StaticTile.Equals(tile));
            var newTile = new StaticTile(tile.Id, tile.X, tile.Y, newZ, tile.Hue, tile.Block);
            StaticTiles.Add(new StaticObject(newTile));
        };
        Client.StaticTileHued += (tile, newHue) =>
        {
            StaticTiles.First(so => so.StaticTile.Equals(tile)).Hue = newHue;
        };
        Client.Moved += (x, y) => Position = new Point(x,y);
        Client.Connected += () =>
        {
            LandTiles.Clear();
            StaticTiles.Clear();
            VirtualLayer.Width = (ushort)(Client.Width * 8);
            VirtualLayer.Height = (ushort)(Client.Height * 8);
        };
        Client.Disconnected += () =>
        {
            LandTiles.Clear();
            StaticTiles.Clear();
        };

        Camera.Position.X = 0;
        Camera.Position.Y = 0;
        Camera.ScreenSize.X = 0;
        Camera.ScreenSize.Y = 0;
        Camera.ScreenSize.Width = gd.PresentationParameters.BackBufferWidth;
        Camera.ScreenSize.Height = gd.PresentationParameters.BackBufferHeight;

        // This has to match the LightDirection below
        _lightSourceCamera.Position = Camera.Position;
        _lightSourceCamera.Zoom = Camera.Zoom;
        _lightSourceCamera.Rotation = 45;
        _lightSourceCamera.ScreenSize.Width = Camera.ScreenSize.Width * 2;
        _lightSourceCamera.ScreenSize.Height = Camera.ScreenSize.Height * 2;

        _lightingState.LightDirection = new Vector3(0, -1, -1f);
        _lightingState.LightDiffuseColor = Vector3.Normalize(new Vector3(1, 1, 1));
        _lightingState.LightSpecularColor = Vector3.Zero;
        _lightingState.AmbientLightColor = new Vector3
        (
            1f - _lightingState.LightDiffuseColor.X,
            1f - _lightingState.LightDiffuseColor.Y,
            1f - _lightingState.LightDiffuseColor.Z
        );
    }

    public void ReloadShader()
    {
        if(File.Exists("MapEffect.fxc")) 
            _mapEffect = new MapEffect(_gfxDevice, File.ReadAllBytes("MapEffect.fxc"));
    }

    public void Load(string clientPath, string clientVersion)
    {
        var valid = ClientVersionHelper.IsClientVersionValid(clientVersion, out UOFileManager.Version);
        UOFileManager.BasePath = clientPath;
        UOFileManager.IsUOPInstallation = UOFileManager.Version >= ClientVersion.CV_7000 && File.Exists
            (UOFileManager.GetUOFilePath("MainMisc.uop"));

        if (!Task.WhenAll
            (
                new List<Task>
                {
                    ArtLoader.Instance.Load(),
                    HuesLoader.Instance.Load(),
                    TileDataLoader.Instance.Load(),
                    TexmapsLoader.Instance.Load(),
                }
            ).Wait(TimeSpan.FromSeconds(10.0)))
            Log.Panic("Loading files timeout.");

        TextureAtlas.InitializeSharedTexture(_gfxDevice);
        HuesManager.Initialize(_gfxDevice);
        RadarMap.Initialize(_gfxDevice);

        var landIds = new List<int>();
        for (int i = 0; i < TileDataLoader.Instance.LandData.Length; i++)
        {
            if (!ArtLoader.Instance.GetValidRefEntry(i).Equals(UOFileIndex.Invalid))
            {
                landIds.Add(i);
            }
        }
        ValidLandIds = landIds.ToArray();
        var staticIds = new List<int>();
        for (int i = 0; i < TileDataLoader.Instance.StaticData.Length; i++)
        {
            if (!ArtLoader.Instance.GetValidRefEntry(i + ArtLoader.MAX_LAND_DATA_INDEX_COUNT).Equals
                    (UOFileIndex.Invalid))
            {
                staticIds.Add(i);
            }
        }
        ValidStaticIds = staticIds.ToArray();
    }

    public Point Position
    {
        get => new((int)(Camera.Position.X * TILE_SIZE), (int)(Camera.Position.Y * TILE_SIZE));
        set {
            if(value != Position) Camera.Moved = true;
            Camera.Position.X = value.X * TILE_SIZE;
            Camera.Position.Y = value.Y * TILE_SIZE;
        }
    }

    private enum MouseDirection
    {
        North,
        Northeast,
        East,
        Southeast,
        South,
        Southwest,
        West,
        Northwest
    }

    // This is all just a fast math way to figure out what the direction of the mouse is.
    private MouseDirection ProcessMouseMovement(ref MouseState mouseState, out float distance)
    {
        Vector2 vec = new Vector2
            (mouseState.X - (Camera.ScreenSize.Width / 2), mouseState.Y - (Camera.ScreenSize.Height / 2));

        int hashf = 100 * (Math.Sign(vec.X) + 2) + 10 * (Math.Sign(vec.Y) + 2);

        distance = vec.Length();
        if (distance == 0)
        {
            return MouseDirection.North;
        }

        vec.X = Math.Abs(vec.X);
        vec.Y = Math.Abs(vec.Y);

        if (vec.Y * 5 <= vec.X * 2)
        {
            hashf += 1;
        }
        else if (vec.Y * 2 >= vec.X * 5)
        {
            hashf += 3;
        }
        else
        {
            hashf += 2;
        }

        switch (hashf)
        {
            case 111: return MouseDirection.Southwest;
            case 112: return MouseDirection.West;
            case 113: return MouseDirection.Northwest;
            case 120: return MouseDirection.Southwest;
            case 131: return MouseDirection.Southwest;
            case 132: return MouseDirection.South;
            case 133: return MouseDirection.Southeast;
            case 210: return MouseDirection.Northwest;
            case 230: return MouseDirection.Southeast;
            case 311: return MouseDirection.Northeast;
            case 312: return MouseDirection.North;
            case 313: return MouseDirection.Northwest;
            case 320: return MouseDirection.Northeast;
            case 331: return MouseDirection.Northeast;
            case 332: return MouseDirection.East;
            case 333: return MouseDirection.Southeast;
        }

        return MouseDirection.North;
    }

    private readonly float WHEEL_DELTA = 1200f;

    public List<LandObject> LandTiles = new();
    public List<LandObject> GhostLandTiles = new();
    public List<StaticObject> StaticTiles = new();
    public List<StaticObject> GhostStaticTiles = new();
    public VirtualLayerObject VirtualLayer = VirtualLayerObject.Instance;
    private MouseState _prevMouseState = Mouse.GetState();
    public Rectangle ViewRange { get; private set; }

    public void Update(GameTime gameTime, bool isActive, bool processMouse, bool processKeyboard)
    {
        Metrics.Start("UpdateMap");
        if (isActive && processMouse)
        {
            var mouse = Mouse.GetState();

            // if (mouse.RightButton == ButtonState.Pressed)
            // {
            //     var direction = ProcessMouseMovement(ref mouse, out var distance);
            //
            //     int delta = distance > 200 ? 10 : 5;
            //     switch (direction)
            //     {
            //         case MouseDirection.North:
            //             Camera.Move(0, -delta);
            //             break;
            //         case MouseDirection.Northeast:
            //             Camera.Move(delta, -delta);
            //             break;
            //         case MouseDirection.East:
            //             Camera.Move(delta, 0);
            //             break;
            //         case MouseDirection.Southeast:
            //             Camera.Move(delta, delta);
            //             break;
            //         case MouseDirection.South:
            //             Camera.Move(0, delta);
            //             break;
            //         case MouseDirection.Southwest:
            //             Camera.Move(-delta, delta);
            //             break;
            //         case MouseDirection.West:
            //             Camera.Move(-delta, 0);
            //             break;
            //         case MouseDirection.Northwest:
            //             Camera.Move(-delta, -delta);
            //             break;
            //     }
            // }

            if (mouse.ScrollWheelValue != _prevMouseState.ScrollWheelValue)
            {
                Camera.ZoomIn((mouse.ScrollWheelValue - _prevMouseState.ScrollWheelValue) / WHEEL_DELTA);
            }

            if (_gfxDevice.Viewport.Bounds.Contains(new Point(mouse.X, mouse.Y)))
            {
                var newTilePos = Unproject(mouse.X, mouse.Y, VirtualLayerZ);
                if (newTilePos != VirtualLayerTilePos)
                {
                    VirtualLayerTilePos = newTilePos;
                    ActiveTool?.OnVirtualLayerTile(VirtualLayerTilePos);
                }
                var newSelected = GetMouseSelection(mouse.X, mouse.Y);
                if (newSelected != Selected)
                {
                    ActiveTool?.OnMouseLeave(Selected);
                    Selected = newSelected;
                    ActiveTool?.OnMouseEnter(Selected);
                }
                if (Selected != null)
                {
                    if (mouse.LeftButton == ButtonState.Pressed)
                    {
                        ActiveTool?.OnMousePressed(Selected);
                    }
                    if (mouse.LeftButton == ButtonState.Released)
                    {
                        ActiveTool?.OnMouseReleased(Selected);
                    }
                }
            }
        }

        if (isActive && processKeyboard)
        {
            var keyboard = Keyboard.GetState();

            foreach (var key in keyboard.GetPressedKeys())
            {
                switch (key)
                {
                    case Keys.Escape:
                        Camera.Zoom = 1;
                        Camera.Moved = true;
                        break;
                    case Keys.A:
                        Camera.Move(-10, 10);
                        break;
                    case Keys.D:
                        Camera.Move(10, -10);
                        break;
                    case Keys.W:
                        Camera.Move(-10, -10);
                        break;
                    case Keys.S:
                        Camera.Move(10, 10);
                        break;
                }
            }
        }

        Camera.Update();
        CalculateViewRange(Camera, out var newViewRange);
        if (ViewRange != newViewRange)
        {
            List<BlockCoords> requested = new List<BlockCoords>();
            for (var x = newViewRange.Left / 8 - 1; x < newViewRange.Right / 8 + 1; x++)
            {
                for (var y = newViewRange.Top / 8 - 1; y < newViewRange.Bottom / 8 + 1; y++)
                {
                    if (ViewRange.Contains(x, y))
                        continue;
                    requested.Add(new BlockCoords((ushort)x, (ushort)y));
                }
            }

            if (Client.Initialized)
            {
                Client.ResizeCache(newViewRange.Width * newViewRange.Height / 8);
                Client.LoadBlocks(requested);
            }

            LandTiles.RemoveAll(o => !newViewRange.Contains(o.Tile.X, o.Tile.Y));
            StaticTiles.RemoveAll(o => !newViewRange.Contains(o.Tile.X, o.Tile.Y));
            
            for (int x = newViewRange.Left; x < newViewRange.Right; x++)
            {
                for (int y = newViewRange.Top; y < newViewRange.Bottom; y++)
                {
                    if (ViewRange.Contains(x, y))
                        continue;
                    LandTiles.Add(new LandObject(Client, Client.GetLandTile(x, y)));
                    var staticTiles = Client.GetStaticTiles(x, y);
                    foreach (var staticTile in staticTiles)
                    {
                        StaticTiles.Add(new StaticObject(staticTile));
                    }
                }
            }
            
            _lightSourceCamera.Position = Camera.Position;
            _lightSourceCamera.Zoom = Camera.Zoom;
            _lightSourceCamera.Rotation = 45;
            _lightSourceCamera.ScreenSize.Width = Camera.ScreenSize.Width * 2;
            _lightSourceCamera.ScreenSize.Height = Camera.ScreenSize.Height * 2;
            _lightSourceCamera.Update();

            ViewRange = newViewRange;
        }

        _prevMouseState = Mouse.GetState();
        Metrics.Stop("UpdateMap");
    }

    public void Reset()
    {
        LandTiles.Clear();
        StaticTiles.Clear();
        ViewRange = Rectangle.Empty;
        Client.ResizeCache(0);
    }

    public TileObject? Selected;

    public TileObject? GetMouseSelection(int x, int y)
    {
        Color[] pixels = new Color[1];
        _selectionBuffer.GetData(0, new Rectangle(x, y, 1, 1), pixels, 0, 1);
        var pixel = pixels[0];
        var selectedIndex = pixel.R | (pixel.G << 8) | (pixel.B << 16);
        if (selectedIndex < 1 || selectedIndex > LandTiles.Count + StaticTiles.Count)
            return null;
        if (selectedIndex > LandTiles.Count)
            return StaticTiles[selectedIndex - 1 - LandTiles.Count];
        return LandTiles[selectedIndex - 1];
    }

    private void CalculateViewRange(Camera camera, out Rectangle rect)
    {
        float zoom = camera.Zoom;
        int screenWidth = camera.ScreenSize.Width;
        int screenHeight = camera.ScreenSize.Height;
        
        /* Calculate the size of the drawing diamond in pixels */
        float screenDiamondDiagonal = (screenWidth + screenHeight) / zoom / 3f;
        
        Vector3 center = camera.Position;
        
        // Render a few extra rows at the top to deal with things at lower z
        var minTileX = Math.Max(0, (int)Math.Ceiling((center.X - screenDiamondDiagonal) / TILE_SIZE) - 8);
        var minTileY = Math.Max(0, (int)Math.Ceiling((center.Y - screenDiamondDiagonal) / TILE_SIZE) - 8);
        
        // Render a few extra rows at the bottom to deal with things at higher z
        var maxTileX = Math.Min
            (Client.Width * 8 - 1, (int)Math.Ceiling((center.X + screenDiamondDiagonal) / TILE_SIZE) + 8);
        var maxTileY = Math.Min
            (Client.Height * 8 - 1, (int)Math.Ceiling((center.Y + screenDiamondDiagonal) / TILE_SIZE) + 8);
        rect = new Rectangle(minTileX, minTileY, maxTileX - minTileX, maxTileY - minTileY);
    }

    public Vector3 Unproject(int x, int y, int z)
    {
        var worldPoint = _gfxDevice.Viewport.Unproject
        (
            new Vector3(x, y, -(z / 256f) + 0.5f),
            Camera.WorldViewProj,
            Matrix.Identity,
            Matrix.Identity
        );
        return new Vector3
        (
            worldPoint.X / MapObject.TILE_SIZE,
            worldPoint.Y / MapObject.TILE_SIZE,
            worldPoint.Z / MapObject.TILE_Z_SCALE
        );
    }

    private bool IsRock(ushort id)
    {
        switch (id)
        {
            case 4945:
            case 4948:
            case 4950:
            case 4953:
            case 4955:
            case 4958:
            case 4959:
            case 4960:
            case 4962: return true;

            default: return id >= 6001 && id <= 6012;
        }
    }

    private bool IsTree(ushort id)
    {
        switch (id)
        {
            case 3274:
            case 3275:
            case 3276:
            case 3277:
            case 3280:
            case 3283:
            case 3286:
            case 3288:
            case 3290:
            case 3293:
            case 3296:
            case 3299:
            case 3302:
            case 3394:
            case 3395:
            case 3417:
            case 3440:
            case 3461:
            case 3476:
            case 3480:
            case 3484:
            case 3488:
            case 3492:
            case 3496:
            case 3230:
            case 3240:
            case 3242:
            case 3243:
            case 3273:
            case 3320:
            case 3323:
            case 3326:
            case 3329:
            case 4792:
            case 4793:
            case 4794:
            case 4795:
            case 12596:
            case 12593:
            case 3221:
            case 3222:
            case 12602:
            case 12599:
            case 3238:
            case 3225:
            case 3229:
            case 12881:
            case 3228:
            case 3227:
            case 39290:
            case 39280:
            case 39219:
            case 39215:
            case 39223:
            case 39288:
            case 39217:
            case 39225:
            case 39284:
            case 46822:
            case 14492: return true;
        }

        return false;
    }

    private bool CanDrawStatic(ushort id)
    {
        if (id >= TileDataLoader.Instance.StaticData.Length)
            return false;

        ref StaticTiles data = ref TileDataLoader.Instance.StaticData[id];

        // Outlands specific?
        // if ((data.Flags & TileFlag.NoDraw) != 0)
        //     return false;

        switch (id)
        {
            case 0x0001:
            case 0x21BC:
            case 0x63D3: return false;

            case 0x9E4C:
            case 0x9E64:
            case 0x9E65:
            case 0x9E7D:
                return ((data.Flags & TileFlag.Background) == 0 && (data.Flags & TileFlag.Surface) == 0
                        // && (data.Flags & TileFlag.NoDraw) == 0
                    );

            case 0x2198:
            case 0x2199:
            case 0x21A0:
            case 0x21A1:
            case 0x21A2:
            case 0x21A3:
            case 0x21A4: return false;
        }
        
        if(StaticFilterEnabled)
        {
            return !(StaticFilterInclusive ^ StaticFilterIds.Contains(id));
        }

        return true;
    }

    private bool ShouldRender(short z)
    {
        return z >= minZ && z <= maxZ;
    }

    private void DrawStatic(StaticObject so, Vector3 hueOverride = default)
    {
        var tile = so.Tile;
        if (!CanDrawStatic(tile.Id))
            return;

        var landTile = Client.GetLandTile(tile.X, tile.Y);
        if (!ShouldRender(tile.Z) || (ShouldRender(landTile.Z) && landTile.Z > tile.Z + 5))
            return;

        _mapRenderer.DrawMapObject(so, hueOverride);
    }

    private void DrawLand(LandObject lo, Vector3 hueOverride = default)
    {
        if (lo.Tile.Id > TileDataLoader.Instance.LandData.Length)
            return;
        if (!ShouldRender(lo.Tile.Z))
            return;

        _mapRenderer.DrawMapObject(lo, hueOverride);
    }

    public void Draw()
    {
        Metrics.Start("DrawMap");
        if (!Client.Initialized)
        {
            DrawBackground();
            return;
        }
        _gfxDevice.Viewport = new Viewport
        (
            0,
            0,
            _gfxDevice.PresentationParameters.BackBufferWidth,
            _gfxDevice.PresentationParameters.BackBufferHeight
        );
        Metrics.Start("DrawShadows");
        DrawShadows();
        Metrics.Stop("DrawShadows");
        Metrics.Start("DrawSelection");
        DrawSelectionBuffer();
        Metrics.Stop("DrawSelection");
        _mapRenderer.SetRenderTarget(null);
        Metrics.Start("DrawLand");
        DrawLand();
        Metrics.Stop("DrawLand");
        Metrics.Start("DrawStatics");
        DrawStatics();
        Metrics.Stop("DrawStatics");
        Metrics.Start("DrawVirtualLayer");
        DrawVirtualLayer();
        Metrics.Stop("DrawVirtualLayer");
        Metrics.Stop("DrawMap");    }

    private void DrawBackground()
    {
        _gfxDevice.SetRenderTarget(null);
        _gfxDevice.Clear(Color.Black);
        _gfxDevice.BlendState = BlendState.AlphaBlend;
        _spriteBatch.Begin();
        var backgroundRect = new Rectangle
        (
            _gfxDevice.PresentationParameters.BackBufferWidth / 2 - _background.Width / 2,
            _gfxDevice.PresentationParameters.BackBufferHeight / 2 - _background.Height / 2,
            _background.Width,
            _background.Height
        );
        _spriteBatch.Draw(_background, backgroundRect, Color.White);
        _spriteBatch.End();
    }

    private void DrawShadows()
    {
        _mapRenderer.SetRenderTarget(_shadowsBuffer);
        if (!ShowShadows)
        {
            return;
        }
        _mapEffect.WorldViewProj = _lightSourceCamera.WorldViewProj;
        _mapEffect.LightSource.Enabled = false;
        _mapEffect.CurrentTechnique = _mapEffect.Techniques["ShadowMap"];
        _mapRenderer.Begin
        (
            _mapEffect,
            _lightSourceCamera,
            RasterizerState.CullNone,
            SamplerState.PointClamp,
            _depthStencilState,
            BlendState.AlphaBlend,
            null,
            null
        );
        foreach (var staticTile in StaticTiles)
        {
            if (!IsRock(staticTile.Tile.Id) && !IsTree(staticTile.Tile.Id) &&
                !TileDataLoader.Instance.StaticData[staticTile.Tile.Id].IsFoliage)
                continue;
            DrawStatic(staticTile);
        }
        foreach (var landTile in LandTiles)
        {
            DrawLand(landTile);
        }
        _mapRenderer.End();
    }

    private void DrawSelectionBuffer()
    {
        _mapEffect.WorldViewProj = Camera.WorldViewProj;
        _mapEffect.CurrentTechnique = _mapEffect.Techniques["Selection"];
        _mapRenderer.SetRenderTarget(_selectionBuffer);
        _mapRenderer.Begin
        (
            _mapEffect,
            Camera,
            RasterizerState.CullNone,
            SamplerState.PointClamp,
            _depthStencilState,
            BlendState.AlphaBlend,
            null,
            null
        );
        //0 is no tile in selection buffer
        var i = 1;
        if (ShowLand)
        {
            foreach (var tile in LandTiles)
            {
                var color = new Color(i & 0xFF, (i >> 8) & 0xFF, (i >> 16) & 0xFF);
                DrawLand(tile, color.ToVector3());
                i++;
            }
        }
        if (ShowStatics)
        {
            foreach (var tile in StaticTiles)
            {
                var color = new Color(i & 0xFF, (i >> 8) & 0xFF, (i >> 16) & 0xFF);
                DrawStatic(tile, color.ToVector3());
                i++;
            }
        }
        _mapRenderer.End();
    }

    private void DrawStatics()
    {
        if (!ShowStatics)
        {
            return;
        }
        _mapEffect.WorldViewProj = Camera.WorldViewProj;
        _mapEffect.CurrentTechnique = _mapEffect.Techniques["Statics"];
        _mapRenderer.Begin
        (
            _mapEffect,
            Camera,
            RasterizerState.CullNone,
            SamplerState.PointClamp,
            _depthStencilState,
            BlendState.AlphaBlend,
            _shadowsBuffer,
            HuesManager.Instance.Texture
        );
        foreach (var tile in StaticTiles)
        {
            if (tile.Visible)
                DrawStatic(tile);
        }
        foreach (var tile in GhostStaticTiles)
        {
            DrawStatic(tile);
        }
        _mapRenderer.End();
    }

    private void DrawLand()
    {
        if (!ShowLand)
        {
            return;
        }
        _mapEffect.WorldViewProj = Camera.WorldViewProj;
        _mapEffect.LightWorldViewProj = _lightSourceCamera.WorldViewProj;
        _mapEffect.AmbientLightColor = _lightingState.AmbientLightColor;
        _mapEffect.LightSource.Direction = _lightingState.LightDirection;
        _mapEffect.LightSource.DiffuseColor = _lightingState.LightDiffuseColor;
        _mapEffect.LightSource.SpecularColor = _lightingState.LightSpecularColor;
        _mapEffect.LightSource.Enabled = true;
        _mapEffect.CurrentTechnique = _mapEffect.Techniques["Terrain"];
        _mapRenderer.Begin
        (
            _mapEffect,
            Camera,
            RasterizerState.CullNone,
            SamplerState.PointClamp,
            _depthStencilState,
            BlendState.AlphaBlend,
            _shadowsBuffer,
            HuesManager.Instance.Texture
        );
           
        foreach (var tile in LandTiles)
        {
            if (tile.Visible)
                DrawLand(tile);
        }
        foreach (var tile in GhostLandTiles)
        {
            DrawLand(tile);
        }
        _mapRenderer.End();
    }

    public void DrawVirtualLayer()
    {
        if (!ShowVirtualLayer)
        {
            return;
        }
        _mapEffect.LightSource.Enabled = false;
        _mapEffect.CurrentTechnique = _mapEffect.Techniques["VirtualLayer"];
        _mapRenderer.Begin
        (
            _mapEffect,
            Camera,
            RasterizerState.CullNone,
            SamplerState.PointClamp,
            _depthStencilState,
            BlendState.AlphaBlend,
            null,
            null
        );
        VirtualLayer.Z = (sbyte)VirtualLayerZ;
        _mapRenderer.DrawMapObject(VirtualLayer, Vector3.Zero);
        _mapRenderer.End();
    }

    //TODO: Bring me back!
    public void DrawHighRes()
    {
        // Console.WriteLine("HIGH RES!");
        // var myRenderTarget = new RenderTarget2D(_gfxDevice, 15360, 8640, false, SurfaceFormat.Color, DepthFormat.Depth24);
        //
        // var myCamera = new Camera();
        // myCamera.Position = Camera.Position;
        // myCamera.Zoom = Camera.Zoom;
        // myCamera.ScreenSize = myRenderTarget.Bounds;
        // myCamera.Update();
        //
        // CalculateViewRange(myCamera, out var minTileX, out var minTileY, out var maxTileX, out var maxTileY);
        // List<BlockCoords> requested = new List<BlockCoords>();
        // for (var x = minTileX / 8; x < maxTileX / 8 + 1; x++) {
        //     for (var y = minTileY / 8; y < maxTileY / 8 + 1; y++) {
        //         requested.Add(new BlockCoords((ushort)x, (ushort)y));
        //     }
        // }
        // Client.ResizeCache(requested.Count * 4);
        // Client.LoadBlocks(requested);
        //
        // _mapEffect.WorldViewProj = myCamera.WorldViewProj;
        // _mapEffect.LightSource.Enabled = false;
        //
        // _mapEffect.CurrentTechnique = _mapEffect.Techniques["Statics"];
        // _mapRenderer.Begin(myRenderTarget, _mapEffect, myCamera, RasterizerState.CullNone, SamplerState.PointClamp,
        //     _depthStencilState, BlendState.AlphaBlend, null, _huesManager.Texture, true);
        // if (IsDrawStatic) {
        //     foreach (var tile in StaticTiles) {
        //         DrawStatic(tile, Vector3.Zero);
        //     }
        // }
        // _mapRenderer.End();
        //
        // _mapEffect.CurrentTechnique = _mapEffect.Techniques["Terrain"];
        // _mapRenderer.Begin(myRenderTarget, _mapEffect, myCamera, RasterizerState.CullNone, SamplerState.PointClamp,
        //     _depthStencilState, BlendState.AlphaBlend, null, _huesManager.Texture, false);
        // if (IsDrawLand) {
        //     foreach (var tile in LandTiles) {
        //         DrawLand(tile, Vector3.Zero);
        //     }
        // }
        // _mapRenderer.End();
        //
        // using var fs = new FileStream(@"C:\git\CentrEDSharp\render.jpg", FileMode.OpenOrCreate);
        // myRenderTarget.SaveAsJpeg(fs, myRenderTarget.Width, myRenderTarget.Height);
        // Console.WriteLine("HIGH RES DONE!");
    }

    public void OnWindowsResized(GameWindow window)
    {
        Camera.ScreenSize = window.ClientBounds;
        Camera.Update();

        _shadowsBuffer = new RenderTarget2D
        (
            _gfxDevice,
            _gfxDevice.PresentationParameters.BackBufferWidth * 2,
            _gfxDevice.PresentationParameters.BackBufferHeight * 2,
            false,
            SurfaceFormat.Single,
            DepthFormat.Depth24
        );

        _selectionBuffer = new RenderTarget2D
        (
            _gfxDevice,
            _gfxDevice.PresentationParameters.BackBufferWidth,
            _gfxDevice.PresentationParameters.BackBufferHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24
        );
    }
}