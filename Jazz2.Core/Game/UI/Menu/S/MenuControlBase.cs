﻿using Duality;
using Duality.Drawing;

namespace Jazz2.Game.UI.Menu.S
{
    public abstract class MenuControlBase
    {
        protected readonly MainMenu api;

        public abstract bool IsEnabled { get; set; }
        public abstract bool IsInputCaptured { get; }

        public MenuControlBase(MainMenu api)
        {
            this.api = api;
        }

        public abstract void OnDraw(Canvas canvas, ref Vector2 pos, bool focused);

        public abstract void OnUpdate();
    }
}