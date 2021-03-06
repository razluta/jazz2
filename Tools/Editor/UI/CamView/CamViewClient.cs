﻿using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Duality;
using Duality.Components;
using Duality.Drawing;
using Duality.Editor.Backend;

namespace Editor.UI.CamView
{
    public abstract class CamViewClient
    {
        private CamView view = null;
        private int pickingFrameLast = -1;


        public CamView View
        {
            get { return this.view; }
            internal set { this.view = value; }
        }

        public Size ClientSize
        {
            get { return this.view.RenderableControl.ClientSize; }
        }
        public Cursor Cursor
        {
            get { return this.view.RenderableControl.Cursor; }
            set { this.view.RenderableControl.Cursor = value; }
        }
        public ColorRgba BgColor
        {
            get { return this.view.BgColor; }
            set { this.view.BgColor = value; }
        }
        public ColorRgba FgColor
        {
            get { return this.view.FgColor; }
        }
        public bool Focused
        {
            get { return this.view.RenderableControl.Focused; }
        }
        internal INativeRenderableSite RenderableSite
        {
            get { return this.view == null ? null : this.view.RenderableSite; }
        }
        internal Control RenderableControl
        {
            get { return this.view == null ? null : this.view.RenderableControl; }
        }

        public Camera CameraComponent
        {
            get { return this.view.CameraComponent; }
        }
        public GameObject CameraObj
        {
            get { return this.view.CameraObj; }
        }

        public Point PointToClient(Point screenCoord)
        {
            return this.view.RenderableControl.PointToClient(screenCoord);
        }
        public Point PointToScreen(Point clientCoord)
        {
            return this.view.RenderableControl.PointToScreen(clientCoord);
        }
        public void Invalidate()
        {
            if (this.view == null || this.view.RenderableControl == null) return;
            this.view.RenderableControl.Invalidate();
        }
        public void Focus()
        {
            this.view.RenderableControl.Focus();
        }

        public ICmpRenderer PickRendererAt(int x, int y)
        {
            this.RenderableSite.MakeCurrent();
            this.RenderPickingMap();
            return this.CameraComponent.PickRendererAt(x / 2, y / 2);
        }
        public IEnumerable<ICmpRenderer> PickRenderersIn(int x, int y, int w, int h)
        {
            this.RenderableSite.MakeCurrent();
            this.RenderPickingMap();
            return this.CameraComponent.PickRenderersIn(x / 2, y / 2, (w + 1) / 2, (h + 1) / 2);
        }
        public bool IsCoordInView(Vector3 c, float boundRad = 1.0f)
        {
            return this.CameraComponent.IsCoordInView(c, boundRad);
        }
        public float GetScaleAtZ(float z)
        {
            return this.CameraComponent.GetScaleAtZ(z);
        }
        public Vector3 GetSpaceCoord(Vector3 screenCoord)
        {
            return this.CameraComponent.GetSpaceCoord(screenCoord);
        }
        public Vector3 GetSpaceCoord(Vector2 screenCoord)
        {
            return this.CameraComponent.GetSpaceCoord(screenCoord);
        }
        public Vector3 GetScreenCoord(Vector3 spaceCoord)
        {
            return this.CameraComponent.GetScreenCoord(spaceCoord);
        }

        public void MakeDualityTarget()
        {
            this.view.MakeDualityTarget();
        }

        private bool RenderPickingMap()
        {
            if (this.pickingFrameLast == Time.FrameCount) return false;
            if (this.ClientSize.IsEmpty) return false;

            this.pickingFrameLast = Time.FrameCount;
            Point2 clientSize = new Point2(this.ClientSize.Width, this.ClientSize.Height);
            this.CameraComponent.RenderPickingMap(
                clientSize / 2,
                clientSize,
                true);

            return true;
        }
    }
}