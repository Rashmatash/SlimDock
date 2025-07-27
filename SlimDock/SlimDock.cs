// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace SlimDock
{
    internal class MouseHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }

            public POINT(Point pt) : this((int)pt.X, (int)pt.Y) { }
            public static implicit operator Point(POINT p) => new Point(p.X, p.Y);
            public static implicit operator POINT(Point p) => new POINT(p);
        }

        [DllImport("User32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
        [DllImport("User32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public static void SetCursor(int x, int y) => SetCursorPos(x, y);

        public static Point GetCursor()
        {
            GetCursorPos(out POINT p);
            return p;
        }
    }
    internal class ThirdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is double) || value == DependencyProperty.UnsetValue) return value;
            return (double)value / 3;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is double) || value == DependencyProperty.UnsetValue) return value;
            return (double)value * 3;
        }
    }
    internal class FirstDocumentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<SlimDockDocument> documents && documents.Any())
            {
                return documents.First().HideSingleDocumentHeader;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SlimDockDocument : ContentControl
    {
        internal SlimDockDocumentPane Owner { get; set; } = null;
        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(SlimDockDocument),
            new PropertyMetadata("title"));

        public bool CanFloat
        {
            get { return (bool)GetValue(CanFloatProperty); }
            set { SetValue(CanFloatProperty, value); }
        }

        public static readonly DependencyProperty CanFloatProperty =
            DependencyProperty.Register("CanFloat", typeof(bool), typeof(SlimDockDocument),
                new PropertyMetadata(true));

        public bool CanClose
        {
            get { return (bool)GetValue(CanCloseProperty); }
            set { SetValue(CanCloseProperty, value); }
        }

        public static readonly DependencyProperty CanCloseProperty =
            DependencyProperty.Register("CanClose", typeof(bool), typeof(SlimDockDocument),
                new PropertyMetadata(true));

        public bool Close
        {
            get { return (bool)GetValue(CloseProperty); }
            set { SetValue(CloseProperty, value); }
        }

        public static readonly DependencyProperty CloseProperty =
            DependencyProperty.Register("Close", typeof(bool), typeof(SlimDockDocument),
                new PropertyMetadata(false, new PropertyChangedCallback(OnCloseChanged)));

        private static void OnCloseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var doc = d as SlimDockDocument;
            if (e.NewValue is bool close && close && doc.Owner != null && doc.CanClose)
            {
                if (doc.Content is FrameworkElement element)
                {
                    element.DataContext = null;
                }
                doc.Owner.RemoveDocument(doc);
            }
        }

        public bool HideSingleDocumentHeader
        {
            get { return (bool)GetValue(HideSingleDocumentHeaderProperty); }
            set { SetValue(HideSingleDocumentHeaderProperty, value); }
        }

        public static readonly DependencyProperty HideSingleDocumentHeaderProperty =
            DependencyProperty.Register("HideSingleDocumentHeader", typeof(bool), typeof(SlimDockDocumentPane),
                new PropertyMetadata(false));
    }

    [ContentProperty("Panel")]
    public class SlimDockControl : ContentControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected internal void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _DragStarted;
        public bool DragStarted
        {
            get { return _DragStarted; }
            internal set
            {
                if (_DragStarted != value)
                {
                    _DragStarted = value;
                    if (RootManager != null) RootManager.DragStarted = value;
                    OnPropertyChanged(nameof(DragStarted));
                }
            }
        }

        public SlimDockPanel Panel
        {
            get { return (SlimDockPanel)GetValue(PanelProperty); }
            set { SetValue(PanelProperty, value); }
        }

        public static readonly DependencyProperty PanelProperty =
            DependencyProperty.Register("Panel", typeof(SlimDockPanel), typeof(SlimDockControl),
                new PropertyMetadata(null, new PropertyChangedCallback(PanelChanged)));

        private static void PanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var manager = d as SlimDockControl;
            if (e.NewValue is SlimDockPanel panel)
            {
                panel.Manager = manager;
                manager.Content = panel;
            }
            else
            {
                if (manager.Window != null)
                {
                    manager.Window.Close();
                    manager.Content = null;
                }
                else
                {
                    manager.Panel = new SlimDockDocumentPane() { Manager = manager };
                }
            }
        }

        internal Guid Id { get; } = Guid.NewGuid();
        internal SlimDockControl RootManager { get; } = null;
        internal Window Window { get; set; } = null;
        internal static SlimDockDocumentPane FocusedDocumentPane { get; set; } = null;

        public SlimDockControl()
        {
            RootManager = this;
        }

        public SlimDockControl(SlimDockControl manager, Window win)
        {
            Contract.Assert(manager != null && win != null && win != Application.Current.MainWindow);
            RootManager = manager.RootManager;
            Id = RootManager.Id;
            RootManager.PropertyChanged += OnRootManagerPropertyChanged;
            DataContext = manager.DataContext;
            Window = win;
        }

        public static SlimDockDocumentPane GetFocusedDocumentPane() => FocusedDocumentPane;

        private void OnRootManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RootManager.DragStarted))
            {
                DragStarted = RootManager.DragStarted;
            }
        }

        internal void Destruct()
        {
            Window = null;
            CloseAllPanels(Panel);
            if (RootManager != null)
            {
                RootManager.PropertyChanged -= OnRootManagerPropertyChanged;
            }
        }

        private static SlimDockPanel CloseAllPanels(SlimDockPanel panel)
        {
            if (panel is SlimDockPane p)
            {
                return CloseAllPanels(p.Panel1) ?? CloseAllPanels(p.Panel2);
            }
            else if (panel is SlimDockDocumentPane dp)
            {
                foreach (var doc in dp.Documents.ToArray())
                {
                    doc.Close = true;
                }
                return dp;
            }
            return null;
        }
    }

    public class SlimDockPanel : Control
    {
        public double PanelMinWidth
        {
            get { return (double)GetValue(PanelMinWidthProperty); }
            set { SetValue(PanelMinWidthProperty, value); }
        }

        public static readonly DependencyProperty PanelMinWidthProperty =
            DependencyProperty.Register("PanelMinWidth", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(0.0));

        public double PanelMaxWidth
        {
            get { return (double)GetValue(PanelMaxWidthProperty); }
            set { SetValue(PanelMaxWidthProperty, value); }
        }

        public static readonly DependencyProperty PanelMaxWidthProperty =
            DependencyProperty.Register("PanelMaxWidth", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(double.PositiveInfinity));

        public double PanelMinHeight
        {
            get { return (double)GetValue(PanelMinHeightProperty); }
            set { SetValue(PanelMinHeightProperty, value); }
        }

        public static readonly DependencyProperty PanelMinHeightProperty =
            DependencyProperty.Register("PanelMinHeight", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(0.0));

        public double PanelMaxHeight
        {
            get { return (double)GetValue(PanelMaxHeightProperty); }
            set { SetValue(PanelMaxHeightProperty, value); }
        }

        public static readonly DependencyProperty PanelMaxHeightProperty =
            DependencyProperty.Register("PanelMaxHeight", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(double.PositiveInfinity));

        public double PanelWidth
        {
            get { return (double)GetValue(PanelWidthProperty); }
            set { SetValue(PanelWidthProperty, value); }
        }

        public static readonly DependencyProperty PanelWidthProperty =
            DependencyProperty.Register("PanelWidth", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(0.0));

        public double PanelHeight
        {
            get { return (double)GetValue(PanelHeightProperty); }
            set { SetValue(PanelHeightProperty, value); }
        }

        public static readonly DependencyProperty PanelHeightProperty =
            DependencyProperty.Register("PanelHeight", typeof(double), typeof(SlimDockPanel),
                new PropertyMetadata(0.0));

        public SlimDockControl Manager { get; set; }

        internal SlimDockPane ParentPanel { get; set; } = null;

        static SlimDockPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SlimDockPanel),
                new FrameworkPropertyMetadata(typeof(SlimDockPanel)));
        }
    }

    public class SlimDockPane : SlimDockPanel
    {
        public SlimDockPanel Panel1
        {
            get { return (SlimDockPanel)GetValue(Panel1Property); }
            set { SetValue(Panel1Property, value); }
        }

        public static readonly DependencyProperty Panel1Property =
            DependencyProperty.Register("Panel1", typeof(SlimDockPanel), typeof(SlimDockPane),
                new PropertyMetadata(null, new PropertyChangedCallback(PanelChanged)));

        public SlimDockPanel Panel2
        {
            get { return (SlimDockPanel)GetValue(Panel2Property); }
            set { SetValue(Panel2Property, value); }
        }

        public static readonly DependencyProperty Panel2Property =
            DependencyProperty.Register("Panel2", typeof(SlimDockPanel), typeof(SlimDockPane),
                new PropertyMetadata(null, new PropertyChangedCallback(PanelChanged)));

        private static void PanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is SlimDockPanel panel)
            {
                var sdPane = d as SlimDockPane;
                Debug.Assert(sdPane.ParentPanel != panel && sdPane != panel);
                Debug.Assert(sdPane.Manager != null);
                if (panel.Manager == null)
                {
                    panel.Manager = sdPane.Manager;
                }
                Debug.Assert(sdPane.Manager.Id == panel.Manager.Id);
                panel.ParentPanel = sdPane;
            }
        }

        public Orientation Orientation
        {
            get { return (Orientation)GetValue(OrientationProperty); }
            set { SetValue(OrientationProperty, value); }
        }

        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register("Orientation", typeof(Orientation), typeof(SlimDockPane),
                new PropertyMetadata(Orientation.Horizontal));

        private void ReplacePanel(SlimDockPanel oldPanel, SlimDockPanel newPanel)
        {
            Debug.Assert(newPanel != null);

            if (Panel1 == oldPanel)
            {
                Panel1 = newPanel;
            }
            else if (Panel2 == oldPanel)
            {
                Panel2 = newPanel;
            }
        }

        internal void RemovePanel(SlimDockPanel panel)
        {
            if (Panel1 == panel)
            {
                Panel1 = null;
                if (Panel2 != null)
                {
                    ParentPanel?.ReplacePanel(this, Panel2);
                    if (ParentPanel == null)
                    {
                        Panel2.ParentPanel = null;
                        Manager.Panel = Panel2;
                    }
                }
            }
            else if (Panel2 == panel)
            {
                Panel2 = null;
                if (Panel1 != null)
                {
                    ParentPanel?.ReplacePanel(this, Panel1);
                    if (ParentPanel == null)
                    {
                        Panel1.ParentPanel = null;
                        Manager.Panel = Panel1;
                    }
                }
            }
        }

        internal SlimDockPane SplitPane(SlimDockPanel panel1, SlimDockPanel panel2, Orientation orientation)
        {
            Debug.Assert(panel1 != null && panel2 != null &&
                (Panel1 == panel1 || Panel2 == panel1 || Panel1 == panel2 || Panel2 == panel2));
            SlimDockPane p = ComposePane(Manager, panel1, panel2, orientation);

            if (Panel1 == panel1 || Panel1 == panel2)
            {
                Panel1 = p;
            }
            else if (Panel2 == panel1 || Panel2 == panel2)
            {
                Panel2 = p;
            }

            return p;
        }

        internal static SlimDockPane ComposePane(SlimDockControl manager, SlimDockPanel panel1, SlimDockPanel panel2, Orientation orientation)
        {
            var p = new SlimDockPane()
            {
                Manager = manager,
                Panel1 = panel1,
                Panel2 = panel2,
                Orientation = orientation,
                PanelWidth = orientation == Orientation.Horizontal ? panel1.PanelWidth + panel2.PanelWidth : Math.Max(panel1.PanelWidth, panel2.PanelWidth),
                PanelHeight = orientation == Orientation.Vertical ? panel1.PanelHeight + panel2.PanelHeight : Math.Max(panel1.PanelHeight, panel2.PanelHeight),
                PanelMinWidth = orientation == Orientation.Horizontal ? panel1.PanelMinWidth + panel2.PanelMinWidth : Math.Max(panel1.PanelMinWidth, panel2.PanelMinWidth),
                PanelMaxWidth = orientation == Orientation.Horizontal ? panel1.PanelMaxWidth + panel2.PanelMaxWidth : Math.Min(panel1.PanelMaxWidth, panel2.PanelMaxWidth),
                PanelMinHeight = orientation == Orientation.Vertical ? panel1.PanelMinHeight + panel2.PanelMinHeight : Math.Max(panel1.PanelMinHeight, panel2.PanelMinHeight),
                PanelMaxHeight = orientation == Orientation.Vertical ? panel1.PanelMaxHeight + panel2.PanelMaxHeight : Math.Min(panel1.PanelMaxHeight, panel2.PanelMaxHeight),
            };
            if (p.PanelMaxWidth < p.PanelMinWidth)
            {
                var min = p.PanelMinWidth;
                p.PanelMinWidth = p.PanelMaxWidth;
                p.PanelMaxWidth = min;
            }
            if (p.PanelMaxHeight < p.PanelMinHeight)
            {
                var min = p.PanelMinHeight;
                p.PanelMinHeight = p.PanelMaxHeight;
                p.PanelMaxHeight = min;
            }

            return p;
        }

        static SlimDockPane()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SlimDockPane),
                new FrameworkPropertyMetadata(typeof(SlimDockPane)));
        }
    }

    [ContentProperty("Documents")]
    [TemplatePart(Name = "PART_tabControl", Type = typeof(TabControl))]
    [TemplatePart(Name = "PART_dropSites", Type = typeof(Grid))]
    [TemplatePart(Name = "PART_center", Type = typeof(Border))]
    [TemplatePart(Name = "PART_top", Type = typeof(Border))]
    [TemplatePart(Name = "PART_left", Type = typeof(Border))]
    [TemplatePart(Name = "PART_bottom", Type = typeof(Border))]
    [TemplatePart(Name = "PART_right", Type = typeof(Border))]
    public class SlimDockDocumentPane : SlimDockPanel
    {
        private static readonly DependencyPropertyKey _DocumentsPropertyKey =
        DependencyProperty.RegisterReadOnly(
          "Documents",
          typeof(ObservableCollection<SlimDockDocument>),
          typeof(SlimDockDocumentPane),
          new FrameworkPropertyMetadata(new ObservableCollection<SlimDockDocument>())
        );
        public static readonly DependencyProperty DocumentsProperty =
            _DocumentsPropertyKey.DependencyProperty;

        public ObservableCollection<SlimDockDocument> Documents
        {
            get { return (ObservableCollection<SlimDockDocument>)GetValue(DocumentsProperty); }
        }

        public Style ItemContainerStyle
        {
            get { return (Style)GetValue(ItemContainerStyleProperty); }
            set { SetValue(ItemContainerStyleProperty, value); }
        }

        public static readonly DependencyProperty ItemContainerStyleProperty =
            DependencyProperty.Register("ItemContainerStyle", typeof(Style), typeof(SlimDockDocumentPane),
                new PropertyMetadata(new Style()));

        private bool _MakeFloating = false;
        private Point _ClickedPosition = new Point(0, 0);
        private UIElement _MouseCaptured = null;
        private Brush _DropSite_BorderBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffccaa00"));
        private Brush _DropSite_BackgroundBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#11ffcc00"));
        private TabControl _DocumentTab = null;

        static SlimDockDocumentPane()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SlimDockDocumentPane),
                  new FrameworkPropertyMetadata(typeof(SlimDockDocumentPane)));
        }

        public SlimDockDocumentPane()
        {
            SetValue(_DocumentsPropertyKey, new ObservableCollection<SlimDockDocument>());
            Documents.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    foreach (SlimDockDocument doc in e.NewItems)
                    {
                        doc.Owner = this;
                    }
                    SelectDocument(Documents.Count - 1);
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    foreach (SlimDockDocument doc in e.OldItems)
                    {
                        doc.Owner = null;
                    }
                    if (Documents.Any())
                        SelectDocument(Documents.Count - 1);
                }
            };

            _DropSite_BackgroundBrush.Freeze();
            _DropSite_BorderBrush.Freeze();

            PreviewMouseDown += (s, e) => { SlimDockControl.FocusedDocumentPane = this; };
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _DocumentTab = GetTemplateChild("PART_tabControl") as TabControl;

            if (_DocumentTab != null)
            {
                var style = new Style(typeof(TabItem), _DocumentTab.ItemContainerStyle);
                style.Setters.Add(new EventSetter(PreviewMouseLeftButtonDownEvent,
                    new MouseButtonEventHandler(OnTabItem_PreviewMouse_LBD)));
                style.Setters.Add(new EventSetter(MouseLeftButtonUpEvent,
                    new MouseButtonEventHandler(OnTabItem_Mouse_LBU)));
                style.Setters.Add(new EventSetter(MouseMoveEvent,
                    new MouseEventHandler(OnTabItem_MouseMove)));
                style.Setters.Add(new EventSetter(QueryContinueDragEvent,
                    new QueryContinueDragEventHandler(OnTabItem_QueryContinueDrag)));
                _DocumentTab.ItemContainerStyle = style;
            }

            if (GetTemplateChild("PART_dropSites") is Grid grid)
            {
                Style style;
                if (grid.FindResource("dropBorderStyle") is Style borderStyle)
                {
                    style = new Style(typeof(Border), borderStyle);
                    _DropSite_BackgroundBrush =
                        (borderStyle.Setters.FirstOrDefault(x => (x as Setter).Property == BackgroundProperty) as Setter)
                        .Value as Brush;
                    _DropSite_BorderBrush =
                        (borderStyle.Setters.FirstOrDefault(x => (x as Setter).Property == BorderBrushProperty) as Setter)
                        .Value as Brush;
                }
                else
                {
                    style = new Style(typeof(Border));
                    style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1.0)));
                }

                style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(BorderBrushProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(AllowDropProperty, true));
                style.Setters.Add(new EventSetter(DragEnterEvent,
                    new DragEventHandler(OnDropSite_DragEnter)));
                style.Setters.Add(new EventSetter(DragLeaveEvent,
                    new DragEventHandler(OnDropSite_DragLeave)));
                style.Setters.Add(new EventSetter(DropEvent,
                    new DragEventHandler(OnDropSite_Drop)));

                grid.Resources.Add(typeof(Border), style);
            }
        }

        internal void AddDocument(SlimDockDocument doc)
        {
            doc.Owner = this;
            Documents.Add(doc);
            SelectDocument(doc);
        }

        internal void RemoveDocument(SlimDockDocument doc)
        {
            Documents.Remove(doc);
            doc.Owner = null;

            if (!Documents.Any())
            {
                ParentPanel?.RemovePanel(this);
                if (ParentPanel == null)
                {
                    Manager.Panel = null;
                }
            }
        }

        public void SelectDocument(SlimDockDocument doc)
        {
            var index = Documents.IndexOf(doc);
            if (index != -1) SelectDocument(index);
        }
        public void SelectDocument(int index)
        {
            Debug.Assert(index != -1 && index < Documents.Count);
            if (index != -1 && _DocumentTab != null) _DocumentTab.SelectedIndex = index;
        }

        private void OnTabItem_PreviewMouse_LBD(object sender, MouseButtonEventArgs e)
        {
            _ClickedPosition = e.GetPosition(this);
            _MouseCaptured = (sender as UIElement);
        }

        private void OnTabItem_Mouse_LBU(object sender, MouseButtonEventArgs e)
        {
            Manager.DragStarted = false;
            _MouseCaptured = null;
        }

        private void OnTabItem_MouseMove(object sender, MouseEventArgs e)
        {
            var tab = sender as TabItem;
            if (e.LeftButton == MouseButtonState.Pressed &&
                tab.Content != null && _MouseCaptured == sender)
            {
                Point mousePos = e.GetPosition(this);
                Vector diff = _ClickedPosition - mousePos;

                if (diff.LengthSquared > 16)
                {
                    var doc = tab.Content as SlimDockDocument;
                    var dataObj = new DataObject(typeof(SlimDockDocument), doc);
                    Manager.DragStarted = true;
                    _MakeFloating = true;
                    var effect = DragDrop.DoDragDrop(tab, dataObj,
                        DragDropEffects.Copy | DragDropEffects.Move);

                    Manager.DragStarted = false;
                    _MouseCaptured = null;

                    if (_MakeFloating && effect == DragDropEffects.None)
                    {
                        MakeFloating(doc);
                    }
                }
            }
        }

        private void OnTabItem_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed)
            {
                _MakeFloating = false;
            }
        }

        internal void MakeFloating(SlimDockDocument doc)
        {
            Debug.Assert(doc != null);
            if (!doc.CanFloat || (doc.Owner == Manager.Panel &&
                doc.Owner != Manager.RootManager.Panel && doc.Owner.Documents.Count == 1))
                return;

            var owner = doc.Owner;
            owner.RemoveDocument(doc);

            var width = owner.ActualWidth;
            var height = owner.ActualHeight;
            var pos = MouseHelper.GetCursor();
            var panel = MakeFloating(Manager, doc, pos.X - 30, pos.Y - 20, width, height, owner);
            panel.PanelWidth = owner.PanelWidth;
            panel.PanelHeight = owner.PanelHeight;
        }

        private static SlimDockDocumentPane MakeFloating(SlimDockControl root, SlimDockDocument doc,
            double x, double y, double width, double height, SlimDockDocumentPane oldOwner)
        {
            if (doc.Owner != null) return null;
            var win = new SlimDockWindow();
            var manager = new SlimDockControl(root, win);
            var panel = new SlimDockDocumentPane()
            {
                Manager = manager,
                PanelWidth = width,
                PanelHeight = height,
            };
            if (oldOwner != null)
            {
                panel.PanelMinWidth = oldOwner.PanelMinWidth;
                panel.PanelMinHeight = oldOwner.PanelMinHeight;
                panel.PanelMaxWidth = oldOwner.PanelMaxWidth;
                panel.PanelMaxHeight = oldOwner.PanelMaxHeight;
                panel.ItemContainerStyle = oldOwner.ItemContainerStyle;
            }
            panel.AddDocument(doc);

            manager.Panel = panel;

            win.Width = panel.PanelWidth;
            win.Height = panel.PanelHeight;
            win.DataContext = root.DataContext;
            win.Content = manager;
            win.Owner = Application.Current.MainWindow;
            win.Left = x;
            win.Top = y;
            win.Resources = win.Owner.Resources;
            win.Background = win.Owner.Background;
            win.Show();
            win.Activate();
            win.Closing += OnSLimDockWindow_Closing;

            return panel;
        }

        public static SlimDockDocumentPane MakeFloating(SlimDockControl root, SlimDockDocument doc,
            double x, double y, double width, double height) =>
            MakeFloating(root, doc, x, y, width, height, null);

        private static void OnSLimDockWindow_Closing(object sender, CancelEventArgs e)
        {
            var manager = (sender as SlimDockWindow).Content as SlimDockControl;
            if (CanClose(manager.Panel))
            {
                manager.Destruct();
            }
            else
            {
                e.Cancel = true;
            }
        }

        private static bool CanClose(SlimDockPanel panel)
        {
            if (panel is SlimDockPane p)
            {
                return CanClose(p.Panel1) && CanClose(p.Panel2);
            }
            else if (panel is SlimDockDocumentPane dp)
            {
                return dp.Documents.FirstOrDefault(x => !x.CanClose) == null;
            }
            return true;
        }

        private void OnDropSite_DragEnter(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            border.BorderBrush = _DropSite_BorderBrush;
            border.Background = _DropSite_BackgroundBrush;
        }

        private void OnDropSite_DragLeave(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            border.BorderBrush = Brushes.Transparent;
            border.Background = Brushes.Transparent;
        }

        private void OnDropSite_Drop(object sender, DragEventArgs e)
        {
            OnDropSite_DragLeave(sender, e);
            Manager.DragStarted = false;
            _MouseCaptured = null;
            var site = (sender as FrameworkElement).Name;

            if (e.Data.GetDataPresent(typeof(SlimDockDocument)))
            {
                var doc = (SlimDockDocument)e.Data.GetData(typeof(SlimDockDocument));
                var owner = doc?.Owner;

                if (doc != null && owner != null && owner.Manager.Id == Manager.Id)
                {
                    if ((owner == this && Documents.Count == 1) ||
                        (!doc.CanFloat && Manager != Manager.RootManager))
                        return;
                    var orientation = (site == "PART_top" || site == "PART_bottom") ?
                        Orientation.Vertical : Orientation.Horizontal;

                    if (site == "PART_center" && !Documents.Contains(doc))
                    {
                        if (!Documents.Any())
                        {
                            PanelWidth = owner.PanelWidth;
                            PanelHeight = owner.PanelHeight;
                            PanelMinWidth = owner.PanelMinWidth;
                            PanelMinHeight = owner.PanelMinHeight;
                            PanelMaxWidth = owner.PanelMaxWidth;
                            PanelMaxHeight = owner.PanelMaxHeight;
                            ItemContainerStyle = owner.ItemContainerStyle;
                        }
                        owner.RemoveDocument(doc);
                        AddDocument(doc);
                    }
                    else if (site == "PART_top" || site == "PART_left")
                    {
                        SplitPanel(doc, orientation, true);
                    }
                    else if (site == "PART_bottom" || site == "PART_right")
                    {
                        SplitPanel(doc, orientation, false);
                    }

                    _MakeFloating = false;
                }
            }
        }

        private void SplitPanel(SlimDockDocument doc, Orientation orientation, bool isTopLeft)
        {
            var owner = doc.Owner;
            owner.RemoveDocument(doc);
            var panel = new SlimDockDocumentPane()
            {
                Manager = Manager,
                PanelWidth = owner.PanelWidth,
                PanelHeight = owner.PanelHeight,
                PanelMinWidth = owner.PanelMinWidth,
                PanelMinHeight = owner.PanelMinHeight,
                PanelMaxWidth = owner.PanelMaxWidth,
                PanelMaxHeight = owner.PanelMaxHeight,
                ItemContainerStyle = owner.ItemContainerStyle,
            };
            panel.AddDocument(doc);

            var p1 = (isTopLeft) ? panel : this;
            var p2 = (isTopLeft) ? this : panel;

            if (ParentPanel != null)
            {
                ParentPanel.SplitPane(p1, p2, orientation);
            }
            else
            {
                Manager.Panel = SlimDockPane.ComposePane(Manager, p1, p2, orientation);
            }
        }
    }

    [TemplatePart(Name = "PART_btnClose", Type = typeof(Button))]
    [TemplatePart(Name = "PART_btnRestore", Type = typeof(Button))]
    [TemplatePart(Name = "PART_btnMinimize", Type = typeof(Button))]
    internal class SlimDockWindow : Window
    {
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_btnClose") is Button btnCLose)
            {
                btnCLose.Click += BtnCLose_Click;
            }
            if (GetTemplateChild("PART_btnRestore") is Button btnRestore)
            {
                btnRestore.Click += BtnRestore_Click;
            }
            if (GetTemplateChild("PART_btnMinimize") is Button btnMinimize)
            {
                btnMinimize.Click += BtnMinimize_Click;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Normal) ?
                WindowState.Maximized : WindowState = WindowState.Normal;
        }

        private void BtnCLose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        static SlimDockWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SlimDockWindow),
                  new FrameworkPropertyMetadata(typeof(SlimDockWindow)));
        }
    }
}
