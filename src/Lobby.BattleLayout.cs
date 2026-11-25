using System;
using System.Drawing;
using System.Windows.Forms;

namespace Boksol
{
    internal sealed partial class Lobby
    {
        private void BuildBattlePage()
        {
            var columns = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Height = 500,
                BackColor = Color.Transparent
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(BuildBattleGameCard(), 0, 0);
            columns.Controls.Add(BuildMatchCard(), 1, 0);
            pages[0].Controls.Add(columns);
        }

        private Panel BuildBattleGameCard()
        {
            var card = SurfacePanel(Surface, DockStyle.Fill);
            card.Margin = new Padding(0, 0, 10, 0);
            card.Width = 400;
            var illustration = new SolarPanel { Artwork = art, Dock = DockStyle.Top, Height = 173 };
            card.Controls.Add(illustration);
            Stretch(card, gameTitle, 22, 187, 22, 48);
            Stretch(card, language, 24, 237, 22, 28);
            Stretch(card, Caption("对战存档", 11, Ink, true), 24, 275, 22, 28);
            Stretch(card, battleSaveButton, 24, 308, 24, 42);
            Stretch(card, steps, 24, 359, 24, 54);
            Place(card, solo, 24, 428, 126, 42);
            Place(card, ActionButton("中文对照", ShowGuide), 162, 428, 126, 42);
            return card;
        }

        private Panel BuildMatchCard()
        {
            var card = SurfacePanel(Surface, DockStyle.Fill);
            card.Margin = new Padding(10, 0, 0, 0);
            card.Width = 550;
            card.Paint += (sender, args) =>
            {
                Line(args.Graphics, Outline, 0, 0, card.Width, 0);
                Line(args.Graphics, Gold, 0, 0, 86, 0);
            };

            matchMode = Caption("局域网配对", 21, Ink, true);
            Stretch(card, matchMode, 26, 24, 26, 48);
            Stretch(card, Caption("房间玩家使用相同的联机码和 ROM 版本。", 10, Quiet), 26, 76, 26, 28);
            Place(card, Caption("联机码", 11, Ink, true), 26, 126, 141, 28);
            Place(card, invitation, 26, 164, card.Width - 200, 40);
            card.SizeChanged += (sender, args) => invitation.Width = Math.Max(30, card.ClientSize.Width - 200);
            AtRight(card, host, 154, 164, 128, 36);
            AtRight(card, copy, 154, 212, 128, 40);
            Place(card, join, 26, 224, 154, 49);
            Place(card, cancel, 192, 224, 106, 49);

            var statusPanel = SurfacePanel(InputColor, DockStyle.None);
            Stretch(card, statusPanel, 26, 295, 26, 88);
            Stretch(statusPanel, roomStatus, 14, 5, 14, 46);
            roomPlayers = Caption("", 10, Mint);
            Stretch(statusPanel, roomPlayers, 14, 49, 14, 34);
            Stretch(card, SurfacePanel(Outline, DockStyle.None), 26, 395, 26, 1);
            Place(card, ActionButton("观战", OpenSpectator), 26, 428, 112, 42);
            Place(card, share, 150, 428, 126, 42);
            return card;
        }
    }
}
