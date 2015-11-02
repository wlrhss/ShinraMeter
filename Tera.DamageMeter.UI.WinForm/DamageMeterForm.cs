﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tera.DamageMeter.UI.Handler;
using Tera.Data;
using Tera.Game;
using Tera.Game.Messages;
using Tera.Sniffing;

namespace Tera.DamageMeter
{
    public partial class DamageMeterForm : Form
    {
        private static readonly BasicTeraData BasicTeraData = new BasicTeraData();
        private static TeraData _teraData;

        private readonly Dictionary<PlayerInfo, PlayerStatsControl> _controls =
            new Dictionary<PlayerInfo, PlayerStatsControl>();

        private readonly KeyboardHook _hook = new KeyboardHook();

        private ClassIcons _classIcons;
        private DamageTracker _damageTracker;
        private EntityTracker _entityRegistry;
        private MessageFactory _messageFactory;
        private PlayerTracker _playerTracker;
        private Server _server;
        private TeraSniffer _teraSniffer;

        public DamageMeterForm()
        {
 
            InitializeComponent();
            Opacity = 0.6;
            BackColor = Color.Green;
            TransparencyKey = Color.Green;
            FormBorderStyle = FormBorderStyle.None;
            Activated += ActionActivate;
            Deactivate += ActionDeactivate;
            RegisterKeyboardHook();

        }

        private void ActionResize(object sender, EventArgs e)
        {
            BasicTeraData.WindowData.Location = DesktopLocation;
            BasicTeraData.WindowData.Size = Size;
        }

        private void ActionActivate(object sender, EventArgs e)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
        }

        private void ActionDeactivate(object sender, EventArgs e)
        {
            FormBorderStyle = FormBorderStyle.None;
        }


        private void hook_KeyPressed(object sender, KeyPressedEventArgs e)
        {
            if (e.Key == BasicTeraData.HotkeysData.Paste.Key && e.Modifier == BasicTeraData.HotkeysData.Paste.Value)
            {
                CopyPaste.Paste();
            }
            else if (e.Key == BasicTeraData.HotkeysData.Reset.Key && e.Modifier == BasicTeraData.HotkeysData.Reset.Value)
            {
                Reset();
            }
            foreach (
                var copy in
                    BasicTeraData.HotkeysData.Copy.Where(copy => e.Key == copy.Key && e.Modifier == copy.Modifier))
            {
                CopyPaste.Copy(PlayerData(), copy.Header, copy.Content, copy.Footer, copy.OrderBy, copy.Order);
            }
        }

        private void RegisterKeyboardHook()
        {
            // register the event that is fired after the key press.
            _hook.KeyPressed += hook_KeyPressed;
            // register the control + alt + F12 combination as hot key.
            try
            {
                _hook.RegisterHotKey(BasicTeraData.HotkeysData.Paste.Value, BasicTeraData.HotkeysData.Paste.Key);
                _hook.RegisterHotKey(BasicTeraData.HotkeysData.Reset.Value, BasicTeraData.HotkeysData.Reset.Key);
            }
            catch
            {
                MessageBox.Show("Cannot bind paste/reset keys");
            }

            try
            {
                foreach (var copy in BasicTeraData.HotkeysData.Copy)
                {
                    _hook.RegisterHotKey(copy.Modifier, copy.Key);
                }
            }
            catch
            {
                MessageBox.Show("Cannot bind copy keys");
            }
        }

        public List<PlayerData> PlayerData()
        {
            return _controls.Select(keyValue => keyValue.Value.PlayerData).ToList();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _classIcons = new ClassIcons(BasicTeraData.ResourceDirectory + @"class-icons\", 36);
            _teraSniffer = new TeraSniffer(BasicTeraData.Servers);
            _teraSniffer.MessageReceived += message => InvokeAction(() => HandleMessageReceived(message));
            _teraSniffer.NewConnection += server => InvokeAction(() => HandleNewConnection(server));
            DesktopLocation = BasicTeraData.WindowData.Location;
            Size = BasicTeraData.WindowData.Size;
            Resize += ActionResize;
            Move += ActionResize;
            _teraSniffer.Enabled = true;
        }

        private void InvokeAction(Action action)
        {
            if (IsDisposed)
                return;
            if (!InvokeRequired)
                throw new InvalidOperationException("Expected InvokeRequired");
            Invoke(action);
        }


        public void Fetch()
        {
            if (_damageTracker != null)
            {
                Fetch(_damageTracker);
            }
        }

        public void Fetch(IEnumerable<PlayerInfo> playerStatsSequence)
        {
            playerStatsSequence =
                playerStatsSequence.OrderByDescending(playerStats => playerStats.Dealt.Damage);
            var totalDamage = playerStatsSequence.Sum(playerStats => playerStats.Dealt.Damage);

            var pos = 0;
            var visiblePlayerStats = new HashSet<PlayerInfo>();
            foreach (var playerStats in playerStatsSequence)
            {
                if (pos > ListPanel.Height)
                    break;

                visiblePlayerStats.Add(playerStats);
                PlayerStatsControl playerStatsControl;
                _controls.TryGetValue(playerStats, out playerStatsControl);
                if (playerStatsControl == null)
                {
                    playerStatsControl = new PlayerStatsControl(playerStats);
                    playerStatsControl.Height = 40;
                    _controls.Add(playerStats, playerStatsControl);
                    playerStatsControl.Parent = ListPanel;
                    playerStatsControl.ClassIcons = _classIcons;
                }
                playerStatsControl.Top = pos;
                playerStatsControl.Width = ListPanel.Width;
                pos += playerStatsControl.Height + 2;
                playerStatsControl.PlayerData.TotalDamage = totalDamage;
                playerStatsControl.Invalidate();
            }

            var invisibleControls = _controls.Where(x => !visiblePlayerStats.Contains(x.Key)).ToList();
            foreach (var invisibleControl in invisibleControls)
            {
                invisibleControl.Value.Dispose();
                _controls.Remove(invisibleControl.Key);
            }
        }

        private void HandleNewConnection(Server server)
        {
            Text = $"{server.Name}";
            _server = server;
            _teraData = BasicTeraData.DataForRegion(server.Region);
            _entityRegistry = new EntityTracker();
            _playerTracker = new PlayerTracker(_entityRegistry);
            _damageTracker = new DamageTracker(_entityRegistry, _playerTracker, _teraData.SkillDatabase);
            _messageFactory = new MessageFactory(_teraData.OpCodeNamer);
        }

        private void HandleMessageReceived(Message obj)
        {
            var message = _messageFactory.Create(obj);
            _entityRegistry.Update(message);

            var skillResultMessage = message as EachSkillResultServerMessage;
            if (skillResultMessage != null)
            {
                _damageTracker.Update(skillResultMessage);
            }
        }

        private void DamageMeterForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            BasicTeraData.WindowData.Save();
            _teraSniffer.Enabled = false;
            Application.Exit();
        }

        private void Reset()
        {
            if (_server == null)
                return;
            _damageTracker = new DamageTracker(_entityRegistry, _playerTracker, _teraData.SkillDatabase);
            Fetch();
        }

        private void RefershTimer_Tick(object sender, EventArgs e)
        {
            Fetch();
        }

        //https://msdn.microsoft.com/en-us/library/ms171548(v=vs.110).aspx
        // Get a handle to an application window.
        [DllImport("USER32.DLL", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName,
            string lpWindowName);

        private void ListPanel_Paint(object sender, PaintEventArgs e)
        {
        }
    }
}