﻿using Cornifer.Json;
using Cornifer.Renderers;
using Cornifer.Structures;
using Cornifer.UI.Elements;
using Cornifer.UI.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cornifer.MapObjects
{
    public abstract class MapObject
    {
        static Regex RandomNameSuffixRegex = new(@"_([0-9A-Fa-f]+)", RegexOptions.Compiled);

        static ShadeRenderer? ShadeTextureRenderer;
        internal static RenderTarget2D? ShadeRenderTarget;

        public virtual bool ParentSelected => Parent != null && (Parent.Selected || Parent.ParentSelected);
        public bool Selected => Main.SelectedObjects.Contains(this);

        public ObjectProperty<bool> ActiveProperty = new("active", true);

        public abstract bool CanSetActive { get; }
        public virtual bool CanCopy { get; } = false;
        public virtual Type CopyType => GetType();

        public virtual bool Active { get => ActiveProperty.Value; set => ActiveProperty.Value = value; }
        public virtual bool Selectable { get; set; } = true;
        public virtual bool LoadCreationForbidden { get; set; } = false;
        public virtual bool NeedsSaving { get; set; } = true;

        public virtual Vector2 ParentPosition { get; set; }
        public virtual Vector2 Size { get; }

        public virtual bool AllowModifyLayer => true;
        public ObjectProperty<Layer, string> RenderLayer = new("layer", null!);
        abstract protected Layer DefaultLayer { get; }

        public Vector2 VisualPosition => WorldPosition + VisualOffset;
        public virtual Vector2 VisualSize => Size + new Vector2(ShadeSize * 2);
        public virtual Vector2 VisualOffset => new Vector2(-ShadeSize);

        public virtual int? ShadeCornerRadius { get; set; }
        public virtual int ShadeSize { get; set; }

        public MapObject? Parent { get; set; }

        public virtual string? Name { get; set; }

        internal UIElement? ConfigCache { get; set; }
        internal Texture2D? ShadeTexture;
        int ShadeTextureShadeSize = 0;

        public bool ShadeTextureDirty { get; set; }
        protected bool Shading { get; private set; }

        public Vector2 WorldPosition
        {
            get => Parent is null ? ParentPosition : Parent.WorldPosition + ParentPosition;
            set
            {
                if (Parent is not null)
                    ParentPosition = value - Parent.WorldPosition;
                else
                    ParentPosition = value;
            }
        }

        public MapObjectCollection Children { get; }

        public MapObject()
        {
            Children = new(this);
            RenderLayer.OriginalValue = DefaultLayer;
            RenderLayer.SaveValue = v => v.Id;
            RenderLayer.LoadValue = v => Main.Layers.FirstOrDefault(l => l.Id == v) ?? RenderLayer.OriginalValue;
        }

        public void DrawShade(Renderer renderer, Layer renderLayer)
        {
            if (!Active)
                return;

            if (renderLayer == RenderLayer.Value)
            {
                EnsureCorrectShadeTexture();

                if (ShadeSize > 0 && ShadeTexture is not null)
                {
                    if (renderer is ICapturingRenderer caps)
                        caps.BeginObjectCapture(this, true);

                    renderer.DrawTexture(ShadeTexture, WorldPosition - new Vector2(ShadeSize));

                    if (renderer is ICapturingRenderer cape)
                        cape.EndObjectCapture();
                }
            }

            foreach (MapObject child in Children)
                child.DrawShade(renderer, renderLayer);
        }

        public void Draw(Renderer renderer, Layer renderLayer)
        {
            if (!Active)
                return;

            if (renderLayer == RenderLayer.Value)
            {
                if (renderer is ICapturingRenderer caps)
                    caps.BeginObjectCapture(this, false);

                DrawSelf(renderer);

                if (renderer is ICapturingRenderer cape)
                    cape.EndObjectCapture();
            }

            foreach (MapObject child in Children)
                child.Draw(renderer, renderLayer);
        }

        protected abstract void DrawSelf(Renderer renderer);

        public UIElement? Config
        {
            get
            {
                ConfigCache ??= BuildConfig();
                UpdateConfig();
                return ConfigCache;
            }
        }

        private UIList? ConfigChildrenList;
        private UIList? ConfigLayerList;

        public static HashSet<string> HideObjectTypes = new()
        {
            "DevToken"
        };

        private UIElement? BuildConfig()
        {
            UIList list = new()
            {
                ElementSpacing = 4,

                Elements =
                {
                    new UILabel
                    {
                        Height = 20,
                        Text = Name,
                        WordWrap = false,
                        TextAlign = new(.5f)
                    },

                    new UICollapsedPanel 
                    {
                        BorderColor = new(100, 100, 100),
                        HeaderText = "Children",
                        Collapsed = true,

                        Content = new UIResizeablePanel
                        {
                            BorderColor = Color.Transparent,
                            BackColor = Color.Transparent,
                            Padding = 4,
                            Height = 100,

                            CanGrabLeft = false,
                            CanGrabRight = false,
                            CanGrabTop = false,

                            MinHeight = 30,

                            Elements =
                            {
                                new UIList
                                {
                                    ElementSpacing = 4,
                                }.Assign(out ConfigChildrenList)
                            }
                        }
                    },
                }
            };

            if (AllowModifyLayer)
            {
                list.Elements.Add(new UICollapsedPanel
                {
                    BorderColor = new(100, 100, 100),
                    HeaderText = "Layer",
                    Collapsed = true,

                    Content = new UIResizeablePanel
                    {
                        BorderColor = Color.Transparent,
                        BackColor = Color.Transparent,
                        Padding = 4,
                        Height = 100,

                        CanGrabLeft = false,
                        CanGrabRight = false,
                        CanGrabTop = false,

                        MinHeight = 30,

                        Elements =
                        {
                            new UIList
                            {
                                ElementSpacing = 4,
                            }.Assign(out ConfigLayerList)
                        }
                    }
                });
            }

            BuildInnerConfig(list);
            return list;
        }
        private void UpdateConfig()
        {
            if (ConfigChildrenList is not null)
            {
                ConfigChildrenList.Elements.Clear();

                if (Children.Count == 0)
                {
                    ConfigChildrenList.Elements.Add(new UILabel
                    {
                        Text = "Empty",
                        Height = 20,
                        TextAlign = new(.5f)
                    });
                }
                else
                {
                    foreach (MapObject obj in Children.OrderBy(o => o.Name))
                    {
                        if (!obj.CanSetActive)
                            continue;

                        UIPanel panel = new()
                        {
                            Padding = 2,
                            Height = 22,
                            BackColor = new(40, 40, 40),

                            Elements =
                            {
                                new UILabel
                                {
                                    Text = obj.Name,
                                    Top = 2,
                                    Left = 2,
                                    Height = 16,
                                    WordWrap = false,
                                    AutoSize = false,
                                    Width = new(-22, 1)
                                },
                                new UIButton
                                {
                                    Text = "A",

                                    Selectable = true,
                                    Selected = obj.ActiveProperty.Value,

                                    SelectedBackColor = Color.White,
                                    SelectedTextColor = Color.Black,

                                    Left = new(0, 1, -1),
                                    Width = 18,
                                }.OnEvent(UIElement.ClickEvent, (btn, _) => obj.ActiveProperty.Value = btn.Selected),
                            }
                        };
                        panel.OnEvent(UIElement.ClickEvent, (p, _) => { if (p.Root?.Hover is not UIButton) Main.FocusOnObject(obj); });
                        ConfigChildrenList.Elements.Add(panel);
                    }
                }
            }
            if (ConfigLayerList is not null)
            {
                ConfigLayerList.Elements.Clear();

                RadioButtonGroup group = new();

                for (int i = Main.Layers.Count - 1; i >= 0; i--)
                {
                    Layer layer = Main.Layers[i];
                    UIButton button = new()
                    {
                        Text = layer.Name,
                        Height = 20,
                        TextAlign = new(.5f),
                        RadioGroup = group,
                        Selectable = true,
                        Selected = layer == RenderLayer.Value,
                        RadioTag = layer,
                        SelectedTextColor = Color.Black,
                        SelectedBackColor = Color.White,
                    };

                    ConfigLayerList.Elements.Add(button);
                }

                group.ButtonClicked += (_, tag) =>
                {
                    if (tag is not Layer layer)
                        return;

                    RenderLayer.Value = layer;
                };
            }

            UpdateInnerConfig();
        }

        public JsonObject? SaveJson(bool forCopy = false)
        {
            if (!NeedsSaving && !forCopy)
                return null;

            JsonNode? inner = SaveInnerJson(forCopy);

            JsonObject json = new();

            if (forCopy)
            {
                json["pos"] = JsonTypes.SaveVector2(WorldPosition);
            }
            else
            {
                json["name"] = Name ?? throw new InvalidOperationException(
                        $"MapObject doesn't have a name and can't be saved.\n" +
                        $"Type: {GetType().Name}\n" +
                        $"Parent: {Parent?.Name ?? Parent?.GetType().Name ?? "null"}");
                json["pos"] = JsonTypes.SaveVector2(ParentPosition);
            }

            ActiveProperty.SaveToJson(json, forCopy);
            RenderLayer.SaveToJson(json, forCopy);

            if (!LoadCreationForbidden)
                json["type"] = forCopy ? CopyType.FullName : GetType().FullName;
            if (inner is not null && (inner is not JsonObject innerobj || innerobj.Count > 0))
                json["data"] = inner;
            if (Children.Count > 0 && !forCopy)
                json["children"] = new JsonArray(Children.Select(c => c.SaveJson()).OfType<JsonNode>().ToArray());

            return json;
        }
        public void LoadJson(JsonNode json, bool shallow)
        {
            if (json.TryGet("data", out JsonNode? data))
                LoadInnerJson(data, shallow);

            if (json.TryGet("name", out string? name))
                Name = name;

            if (json.TryGet("pos", out JsonNode? pos))
                ParentPosition = JsonTypes.LoadVector2(pos);

            ActiveProperty.LoadFromJson(json);
            RenderLayer.LoadFromJson(json);

            if (json.TryGet("children", out JsonArray? children))
                foreach (JsonNode? childNode in children)
                    if (childNode is not null)
                        LoadObject(childNode, Children, shallow);
        }

        public void EnsureCorrectShadeTexture()
        {
            if ((ShadeTexture is null || ShadeTextureDirty || ShadeSize != ShadeTextureShadeSize) && ShadeSize > 0)
            {
                Shading = true;
                GenerateShadeTexture();
                Shading = false;
                ShadeTextureShadeSize = ShadeSize;
            }
            ShadeTextureDirty = false;
        }

        protected virtual void GenerateShadeTexture()
        {
            GenerateDefaultShadeTexture(ref ShadeTexture, this, ShadeSize, ShadeCornerRadius);
        }

        protected static void GenerateDefaultShadeTexture(ref Texture2D? texture, MapObject obj, int shade, int? cornerRadius)
        {
            obj.Shading = true;
            ShadeTextureRenderer ??= new(Main.SpriteBatch);

            Vector2 shadeSize = obj.Size + new Vector2(shade * 2);

            int shadeWidth = (int)Math.Ceiling(shadeSize.X);
            int shadeHeight = (int)Math.Ceiling(shadeSize.Y);

            if (ShadeRenderTarget is null || ShadeRenderTarget.Width < shadeWidth || ShadeRenderTarget.Height < shadeHeight)
            {
                int targetWidth = shadeWidth;
                int targetHeight = shadeHeight;

                if (ShadeRenderTarget is not null)
                {
                    targetWidth = Math.Max(targetWidth, ShadeRenderTarget.Width);
                    targetHeight = Math.Max(targetHeight, ShadeRenderTarget.Height);

                    ShadeRenderTarget?.Dispose();
                }
                ShadeRenderTarget = new(Main.Instance.GraphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }

            ShadeTextureRenderer.TargetNeedsClear = true;
            ShadeTextureRenderer.Position = obj.WorldPosition - new Vector2(shade);

            obj.DrawSelf(ShadeTextureRenderer);

            int shadePixels = shadeWidth * shadeHeight;
            Color[] pixels = ArrayPool<Color>.Shared.Rent(shadePixels);

            // no texture draw calls
            if (ShadeTextureRenderer.TargetNeedsClear)
            {
                Array.Clear(pixels);
            }
            else
            {
                ShadeRenderTarget.GetData(0, new(0, 0, shadeWidth, shadeHeight), pixels, 0, shadePixels);
                ProcessShade(pixels, shadeWidth, shadeHeight, shade, cornerRadius);
            }
            if (texture is null || texture.Width != shadeWidth || texture.Height != shadeHeight)
            {
                texture?.Dispose();
                texture = new(Main.Instance.GraphicsDevice, shadeWidth, shadeHeight);
            }
            texture.SetData(pixels, 0, shadePixels);
            ArrayPool<Color>.Shared.Return(pixels);
            obj.Shading = false;
        }

        protected virtual JsonNode? SaveInnerJson(bool forCopy) => null;
        protected virtual void LoadInnerJson(JsonNode node, bool shallow) { }

        protected virtual void BuildInnerConfig(UIList list) { }
        protected virtual void UpdateInnerConfig() { }

        public virtual void RegenerateName()
        {
            if (Name is null)
            {
                Name = $"{GetType().Name}_{Random.Shared.Next():x}";
                return;
            }

            string random = $"_{Random.Shared.Next():x}";

            Match match = RandomNameSuffixRegex.Match(Name);
            if (match.Success)
            {
                Name = match.Result(random);
                return;
            }

            Name += random;
        }

        public bool ContainsPoint(Vector2 worldPoint)
        {
            return VisualPosition.X <= worldPoint.X
                 && VisualPosition.Y <= worldPoint.Y
                 && VisualPosition.X + VisualSize.X > worldPoint.X
                 && VisualPosition.Y + VisualSize.Y > worldPoint.Y;
        }

        protected static void ProcessShade(Color[] colors, int width, int height, int size, int? cornerRadius)
        {
            int arraysize = width * height;
            bool[] shade = ArrayPool<bool>.Shared.Rent(arraysize);

            int patternSide = size * 2 + 1;

            bool[] shadePattern = null!;

            if (cornerRadius.HasValue)
            {
                shadePattern = ArrayPool<bool>.Shared.Rent(patternSide * patternSide);

                int patternRadSq = cornerRadius.Value * cornerRadius.Value;

                for (int j = 0; j < patternSide; j++)
                    for (int i = 0; i < patternSide; i++)
                    {
                        float lengthsq = (size - i) * (size - i) + (size - j) * (size - j);
                        shadePattern[i + patternSide * j] = lengthsq <= patternRadSq;
                    }
            }

            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    int index = width * j + i;

                    shade[index] = false;

                    if (colors[index].A > 0)
                    {
                        shade[index] = true;
                        continue;
                    }

                    if (size <= 0)
                        continue;

                    bool probing = true;
                    for (int l = -size; l <= size && probing; l++)
                        for (int k = -size; k <= size && probing; k++)
                        {
                            if (cornerRadius.HasValue)
                            {
                                int patternIndex = (l + size) * patternSide + k + size;
                                if (!shadePattern[patternIndex])
                                    continue;
                            }

                            int x = i + k;
                            int y = j + l;

                            if (x < 0 || y < 0 || x >= width || y >= height || k == 0 && l == 0)
                                continue;

                            int testIndex = width * y + x;

                            if (colors[testIndex].A > 0)
                            {
                                shade[index] = true;
                                probing = false;
                                continue;
                            }
                        }
                }

            for (int i = 0; i < arraysize; i++)
                colors[i] = shade[i] ? Color.Black : Color.Transparent;

            ArrayPool<bool>.Shared.Return(shade);
            if (cornerRadius.HasValue)
                ArrayPool<bool>.Shared.Return(shadePattern);
        }

        public static MapObject? FindSelectableAtPos(IEnumerable<MapObject> objects, Vector2 pos, bool searchChildren)
        {
            foreach (MapObject obj in objects.SmartReverse())
            {
                if (!obj.Active || !obj.RenderLayer.Value.Visible)
                    continue;

                if (searchChildren)
                {
                    MapObject? child = FindSelectableAtPos(obj.Children, pos, true);
                    if (child is not null)
                        return child;
                }

                if (obj.ContainsPoint(pos))
                    return obj;
            }
            return null;
        }
        public static IEnumerable<MapObject> FindIntersectingSelectables(IEnumerable<MapObject> objects, Vector2 tl, Vector2 br, bool searchChildren)
        {
            foreach (MapObject obj in objects.SmartReverse())
            {
                if (!obj.Active || !obj.RenderLayer.Value.Visible)
                    continue;

                if (searchChildren)
                    foreach (MapObject child in FindIntersectingSelectables(obj.Children, tl, br, true))
                        yield return child;

                bool intersects = obj.VisualPosition.X < br.X
                    && tl.X < obj.VisualPosition.X + obj.VisualSize.X
                    && obj.VisualPosition.Y < br.Y
                    && tl.Y < obj.VisualPosition.Y + obj.VisualSize.Y;
                if (intersects)
                    yield return obj;
            }
        }

        public static bool LoadObject(JsonNode node, IEnumerable<MapObject> objEnumerable, bool shallow)
        {
            if (!node.TryGet("name", out string? name))
                return false;

            MapObject? obj = objEnumerable.FirstOrDefault(o => o.Name == name);
            if (obj is null)
                return false;

            obj.LoadJson(node, shallow);
            return true;
        }
        public static MapObject? CreateObject(JsonNode node, bool shallow)
        {
            if (!node.TryGet("type", out string? typeName))
                return null;

            Type? type = Type.GetType(typeName);
            if (type is null || !type.IsAssignableTo(typeof(MapObject)))
                return null;

            MapObject instance = (MapObject)Activator.CreateInstance(type)!;

            if (node.TryGet("name", out string? name))
                instance.Name = name;
            else if (instance.Name is null)
                instance.RegenerateName();

            instance.LoadJson(node, shallow);
            return instance;
        }

        public override string? ToString()
        {
            return Name ?? base.ToString();
        }

        public class MapObjectCollection : ICollection<MapObject>
        {
            List<MapObject> Objects = new();
            MapObject Parent;

            public MapObjectCollection(MapObject parent)
            {
                Parent = parent;
            }

            public int Count => Objects.Count;
            public bool IsReadOnly => false;

            public void Add(MapObject item)
            {
                item.Parent?.Children.Remove(item);
                item.Parent = Parent;
                Objects.Add(item);
            }

            public void Clear()
            {
                foreach (MapObject obj in Objects)
                    obj.Parent = null;

                Objects.Clear();
            }

            public bool Remove(MapObject item)
            {
                item.Parent = null;
                return Objects.Remove(item);
            }

            public bool Contains(MapObject item)
            {
                return Objects.Contains(item);
            }

            public void CopyTo(MapObject[] array, int arrayIndex)
            {
                Objects.CopyTo(array, arrayIndex);
            }

            public IEnumerator<MapObject> GetEnumerator()
            {
                return Objects.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return Objects.GetEnumerator();
            }
        }
    }
}
