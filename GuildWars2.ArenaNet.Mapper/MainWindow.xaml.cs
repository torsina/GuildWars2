﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using Microsoft.Maps.MapControl.WPF;
using Location = Microsoft.Maps.MapControl.WPF.Location;

using GuildWars2.ArenaNet.API;
using GuildWars2.ArenaNet.Model;

namespace GuildWars2.ArenaNet.Mapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MumbleLink m_Link;
        private IDictionary<int, FloorMapDetails> m_MapData;

        private volatile bool m_Running;
        private ManualResetEvent m_Canceled;
        private Thread m_PlayerWorkerThread;
        private Thread m_EventWorkerThread;

        private volatile bool m_FollowPlayer;
        private Pushpin m_Player;

        // map layers
        private MapLayer m_MapLayerContainer;
        private IDictionary<int, MapLayer> m_MapLayers;
        private IDictionary<int, MapLayer> m_MapWaypoints;
        private IDictionary<int, MapLayer> m_MapPointsOfInterest;
        private IDictionary<int, MapLayer> m_MapVistas;
        private IDictionary<int, MapLayer> m_MapRenownHearts;
        private IDictionary<int, MapLayer> m_MapSkillPoints;

        private IDictionary<int, MapLayer> m_MapBounties;

        private IDictionary<int, MapLayer> m_MapEvents;
        private IDictionary<Guid, EventPushpin> m_EventPushpins;
        private IDictionary<Guid, EventMapPolygon> m_EventMapPolygons;

        public MainWindow()
        {
            InitializeComponent();

            m_Link = new MumbleLink();
            m_MapData = new Dictionary<int, FloorMapDetails>();

            m_FollowPlayer = false;
            m_Player = new Pushpin();
            m_Player.Template = (ControlTemplate)Application.Current.Resources["PlayerPushpin"];
            m_Player.PositionOrigin = PositionOrigin.Center;
            m_Player.Visibility = Visibility.Hidden;
            m_Map.Children.Add(m_Player);

            m_MapLayerContainer = new MapLayer();
            m_Map.Children.Add(m_MapLayerContainer);

            m_MapLayers = new Dictionary<int, MapLayer>();
            m_MapWaypoints = new Dictionary<int, MapLayer>();
            m_MapPointsOfInterest = new Dictionary<int, MapLayer>();
            m_MapVistas = new Dictionary<int, MapLayer>();
            m_MapRenownHearts = new Dictionary<int, MapLayer>();
            m_MapSkillPoints = new Dictionary<int, MapLayer>();

            m_MapBounties = new Dictionary<int, MapLayer>();

            m_MapEvents = new Dictionary<int, MapLayer>();
            m_EventPushpins = new Dictionary<Guid, EventPushpin>();
            m_EventMapPolygons = new Dictionary<Guid, EventMapPolygon>();

            MapFloorResponse floor = new MapFloorRequest(1, 2).Execute();
            if (floor != null)
            {
                foreach (FloorRegion region in floor.Regions.Values)
                {
                    foreach (string mapId in region.Maps.Keys)
                    {
                        int mid = int.Parse(mapId);
                        FloorMapDetails map = region.Maps[mapId];

                        m_MapData.Add(mid, map);

                        m_MapLayers.Add(mid, new MapLayer());
                        m_MapWaypoints.Add(mid, new MapLayer());
                        m_MapPointsOfInterest.Add(mid, new MapLayer());
                        m_MapVistas.Add(mid, new MapLayer());
                        m_MapRenownHearts.Add(mid, new MapLayer());
                        m_MapSkillPoints.Add(mid, new MapLayer());

                        m_MapLayers[mid].Children.Add(m_MapWaypoints[mid]);
                        m_MapLayers[mid].Children.Add(m_MapPointsOfInterest[mid]);
                        m_MapLayers[mid].Children.Add(m_MapVistas[mid]);
                        m_MapLayers[mid].Children.Add(m_MapRenownHearts[mid]);
                        m_MapLayers[mid].Children.Add(m_MapSkillPoints[mid]);

                        foreach (PointOfInterest poi in map.PointsOfInterest)
                        {
                            Pushpin poiPin = new PointOfInterestPushpin(poi);

                            poiPin.Location = m_Map.Unproject(new Point(poi.Coord[0], poi.Coord[1]), m_Map.MaxZoomLevel);

                            switch (poi.TypeEnum)
                            {
                                case PointOfInterestType.Waypoint:
                                    m_MapWaypoints[mid].Children.Add(poiPin);
                                    break;
                                case PointOfInterestType.Landmark:
                                    m_MapPointsOfInterest[mid].Children.Add(poiPin);
                                    break;
                                case PointOfInterestType.Vista:
                                    m_MapVistas[mid].Children.Add(poiPin);
                                    break;
                                default:
                                    continue;
                            }
                        }

                        foreach (Task rh in map.Tasks)
                        {
                            Pushpin rhPin = new TaskPushpin(rh);

                            rhPin.Location = m_Map.Unproject(new Point(rh.Coord[0], rh.Coord[1]), m_Map.MaxZoomLevel);

                            m_MapRenownHearts[mid].Children.Add(rhPin);
                        }

                        foreach (MappedModel sp in map.SkillChallenges)
                        {
                            Pushpin spPin = new SkillChallengePushpin(sp);

                            spPin.Location = m_Map.Unproject(new Point(sp.Coord[0], sp.Coord[1]), m_Map.MaxZoomLevel);

                            m_MapSkillPoints[mid].Children.Add(spPin);
                        }
                    }
                }
            }

            IList<SolidColorBrush> bounty_path_brushes = new List<SolidColorBrush> { Brushes.Blue, Brushes.White, Brushes.Yellow, Brushes.LimeGreen };
            foreach (GuildBounty bounty in GuildBountyDefinitions.BOUNTIES)
            {
                BountyMapLayer b = new BountyMapLayer(bounty.Name);

                if (!m_MapBounties.ContainsKey(bounty.MapId))
                {
                    m_MapBounties.Add(bounty.MapId, new MapLayer());

                    // map bounties default to hidden
                    m_MapBounties[bounty.MapId].Visibility = Visibility.Hidden;

                    // we insert instead of add so events always show up under other pushpins
                    m_MapLayers[bounty.MapId].Children.Insert(0, m_MapBounties[bounty.MapId]);
                }

                if (bounty.Spawns != null)
                {
                    foreach (List<double> p in bounty.Spawns)
                        b.AddSpawningPoint(m_Map.Unproject(new Point(p[0], p[1]), m_Map.MaxZoomLevel));
                }


                if (bounty.Paths != null)
                {
                    int i = 0;
                    foreach (GuildBountyPath path in bounty.Paths)
                    {
                        LocationCollection locs = new LocationCollection();
                        foreach (List<double> p in path.Points)
                            locs.Add(m_Map.Unproject(new Point(p[0], p[1]), m_Map.MaxZoomLevel));
                        b.AddPath(locs, bounty_path_brushes[i], path.Direction);
                        i = (i + 1) % bounty_path_brushes.Count;
                    }
                }

                m_MapBounties[bounty.MapId].Children.Add(b);
            }

            EventDetailsResponse events = new EventDetailsRequest().Execute();
            if (events != null)
            {
                foreach (KeyValuePair<string, EventDetails> entry in events.Events)
                {
                    Guid eid = new Guid(entry.Key);
                    EventDetails ev = entry.Value;

                    if (!ev.Name.StartsWith("skill challenge: ", StringComparison.InvariantCultureIgnoreCase) && m_MapData.ContainsKey(ev.MapId))
                    {
                        if (!m_MapEvents.ContainsKey(ev.MapId))
                        {
                            m_MapEvents.Add(ev.MapId, new MapLayer());

                            // we insert instead of add so events always show up under other pushpins
                            m_MapLayers[ev.MapId].Children.Insert(0, m_MapEvents[ev.MapId]);
                        }

                        FloorMapDetails map = m_MapData[ev.MapId];

                        Point center = new Point(TranslateX(ev.Location.Center[0], map.MapRect, map.ContinentRect),
                                    TranslateZ(ev.Location.Center[1], map.MapRect, map.ContinentRect));

                        switch(ev.Location.TypeEnum)
                        {
                            case LocationType.Poly:
                                EventMapPolygon evPoly = new EventMapPolygon(ev);

                                foreach (List<double> pt in ev.Location.Points)
                                {
                                    evPoly.Locations.Add(
                                            m_Map.Unproject(
                                                    new Point(
                                                            TranslateX(pt[0], map.MapRect, map.ContinentRect),
                                                            TranslateZ(pt[1], map.MapRect, map.ContinentRect)),
                                                    m_Map.MaxZoomLevel));
                                }

                                m_EventMapPolygons[eid] = evPoly;
                                // insert so polys are below all pushpins
                                m_MapEvents[ev.MapId].Children.Insert(0, evPoly);
                                break;

                            case LocationType.Sphere:
                            case LocationType.Cylinder:
                                EventMapPolygon evCircle = new EventMapPolygon(ev);

                                double radius = TranslateX(ev.Location.Center[0] + ev.Location.Radius, map.MapRect, map.ContinentRect) - center.X;

                                for (int i = 0; i < 360; i+=10)
                                {
                                    evCircle.Locations.Add(
                                            m_Map.Unproject(
                                                    new Point(
                                                            center.X + radius * Math.Cos(i * (Math.PI / 180)),
                                                            center.Y + radius * Math.Sin(i * (Math.PI / 180))),
                                                    m_Map.MaxZoomLevel));
                                }

                                m_EventMapPolygons[eid] = evCircle;
                                // insert so polys are below all pushpins
                                m_MapEvents[ev.MapId].Children.Insert(0, evCircle);
                                break;

                            default:
                                break;
                        }

                        EventPushpin evPin = new EventPushpin(ev);
                        evPin.Location = m_Map.Unproject(center, m_Map.MaxZoomLevel);
                        m_EventPushpins[eid] = evPin;
                        m_MapEvents[ev.MapId].Children.Add(evPin);
                    }
                }
            }

            MouseDown += DrawPolyMouseDownHandler;
            KeyDown += DrawPolyKeyDownHandler;
            KeyUp += DrawPolyKeyUpHandler;

            m_Running = true;
            m_Canceled = new ManualResetEvent(false);
            m_PlayerWorkerThread = new Thread(PlayerWorkerThread);
            m_PlayerWorkerThread.Start();
            m_EventWorkerThread = new Thread(EventWorkerThread);
            m_EventWorkerThread.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            m_Running = false;
            m_Canceled.Set();
            m_PlayerWorkerThread.Join();
            m_EventWorkerThread.Join();

            base.OnClosed(e);
        }

        private int GetMapByCenter(Point center)
        {
            foreach (KeyValuePair<int, FloorMapDetails> entry in m_MapData)
            {
                if (center.X > entry.Value.ContinentRect[0][0] &&
                        center.X < entry.Value.ContinentRect[1][0] &&
                        center.Y > entry.Value.ContinentRect[0][1] &&
                        center.Y < entry.Value.ContinentRect[1][1])
                    return entry.Key;
            }

            return -1;
        }

        private void SetMapLayerVisibility(IDictionary<int, MapLayer> layerDict, Visibility visibility)
        {
            if (layerDict == null)
                return;

            foreach (MapLayer layer in layerDict.Values)
                layer.Visibility = visibility;
        }

        private double TranslateX(double x, List<List<double>> mapRect, List<List<double>> continentRect)
        {
            return (x - mapRect[0][0]) / (mapRect[1][0] - mapRect[0][0]) * (continentRect[1][0] - continentRect[0][0]) + continentRect[0][0];
        }

        private double TranslateZ(double z, List<List<double>> mapRect, List<List<double>> continentRect)
        {
            return (1 - ((z - mapRect[0][1]) / (mapRect[1][1] - mapRect[0][1]))) * (continentRect[1][1] - continentRect[0][1]) + continentRect[0][1];
        }

        #region Worker Threads
        private void PlayerWorkerThread()
        {
            while (m_Running)
            {
                try
                {
                    if (m_Link.DataAvailable && m_MapData.ContainsKey(m_Link.Map))
                    {
                        FloorMapDetails map = m_MapData[m_Link.Map];

                        // convert back to inches
                        double posX = m_Link.PositionX * 39.3700787;
                        double posZ = m_Link.PositionZ * 39.3700787;
                        double rot = m_Link.RotationPlayer;

                        posX = TranslateX(posX, map.MapRect, map.ContinentRect);
                        posZ = TranslateZ(posZ, map.MapRect, map.ContinentRect);

                        Location loc = m_Map.Unproject(new Point(posX, posZ), m_Map.MaxZoomLevel);

                        // move the player icon
                        Dispatcher.Invoke(() =>
                            {
                                m_Player.Heading = rot;
                                m_Player.Visibility = Visibility.Visible;

                                // only follow player if they've asked to and the location has changed
                                if (m_FollowPlayer && m_Player.Location != loc)
                                    m_Map.SetView(loc, m_Map.ZoomLevel);

                                m_Player.Location = loc;
                            }, DispatcherPriority.Background, CancellationToken.None, new TimeSpan(0, 0, 1));
                    }
                }
                catch
                { }

                m_Canceled.WaitOne(100);
            }
        }

        private void EventWorkerThread()
        {
            while (m_Running)
            {
                try
                {
                    EventsResponse events = new EventsRequest(1007).Execute();

                    Dispatcher.Invoke(() =>
                        {
                            foreach (EventState ev in events.Events)
                            {
                                if (m_EventPushpins.ContainsKey(ev.EventId))
                                    m_EventPushpins[ev.EventId].SetEventState(ev.StateEnum);

                                if (m_EventMapPolygons.ContainsKey(ev.EventId))
                                    m_EventMapPolygons[ev.EventId].SetEventState(ev.StateEnum);
                            }
                        }, DispatcherPriority.Background, CancellationToken.None, new TimeSpan(0, 0, 25));
                }
                catch
                { }

                m_Canceled.WaitOne(30000);
            }
        }
        #endregion

        #region Map Handlers
        private void Map_UpdateView(double zoomLevel)
        {
            m_MapLayerContainer.Children.Clear();

            if (zoomLevel >= 3)
            {
                int mid = GetMapByCenter(m_Map.Project(m_Map.Center, m_Map.MaxZoomLevel));
                if (m_MapLayers.ContainsKey(mid))
                {
                    m_MapLayerContainer.Children.Add(m_MapLayers[mid]);
                }
            }

            m_ZoomInButton.IsEnabled = (zoomLevel < m_Map.MaxZoomLevel);
            m_ZoomOutButton.IsEnabled = (zoomLevel > m_Map.MinZoomLevel);
        }

        private void Map_ViewChangeOnFrame(object sender, MapEventArgs e)
        {
            Map_UpdateView(m_Map.TargetZoomLevel);
        }

        private void Map_ViewChangeEnd(object sender, MapEventArgs e)
        {
            Map_UpdateView(m_Map.ZoomLevel);
        }
        #endregion

        #region Legend Checkbox Handlers
        private void LegendIcon_MouseEnter(object sender, MouseEventArgs e)
        {
            m_LegendIcon.Visibility = Visibility.Collapsed;
            m_Legend.Visibility = Visibility.Visible;
        }

        private void Legend_MouseLeave(object sender, MouseEventArgs e)
        {
            m_Legend.Visibility = Visibility.Collapsed;
            m_LegendIcon.Visibility = Visibility.Visible;
        }

        private void Legend_WaypointsChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapWaypoints, Visibility.Visible); }
        private void Legend_WaypointsUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapWaypoints, Visibility.Hidden); }

        private void Legend_PointsOfInterestChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapPointsOfInterest, Visibility.Visible); }
        private void Legend_PointsOfInterestUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapPointsOfInterest, Visibility.Hidden); }

        private void Legend_VistasChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapVistas, Visibility.Visible); }
        private void Legend_VistasUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapVistas, Visibility.Hidden); }

        private void Legend_RenownHeartsChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapRenownHearts, Visibility.Visible); }
        private void Legend_RenownHeartsUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapRenownHearts, Visibility.Hidden); }

        private void Legend_SkillPointsChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapSkillPoints, Visibility.Visible); }
        private void Legend_SkillPointsUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapSkillPoints, Visibility.Hidden); }

        private void Legend_BountiesChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapBounties, Visibility.Visible); }
        private void Legend_BountiesUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapBounties, Visibility.Hidden); }

        private void Legend_EventsChecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapEvents, Visibility.Visible); }
        private void Legend_EventsUnchecked(object sender, RoutedEventArgs e) { SetMapLayerVisibility(m_MapEvents, Visibility.Hidden); }

        private void Legend_FollowPlayerChecked(object sender, RoutedEventArgs e) { m_FollowPlayer = true; }
        private void Legend_FollowPlayerUnchecked(object sender, RoutedEventArgs e) { m_FollowPlayer = false; }
        #endregion

        #region Zoom Handlers
        private void Zoom_InClicked(object sender, RoutedEventArgs e)
        {
            double newZoomLevel = Math.Min(m_Map.ZoomLevel + 1.0, m_Map.MaxZoomLevel);

            if (newZoomLevel != m_Map.ZoomLevel)
                m_Map.SetView(newZoomLevel, m_Map.Heading);
        }

        private void Zoom_OutClicked(object sender, RoutedEventArgs e)
        {
            double newZoomLevel = Math.Max(m_Map.ZoomLevel - 1.0, m_Map.MinZoomLevel);

            if (newZoomLevel != m_Map.ZoomLevel)
                m_Map.SetView(newZoomLevel, m_Map.Heading);
        }
        #endregion

        #region DrawPolyHandlers
        private bool m_DrawingPolygon = false;
        private MapPolygon m_DrawingPolygonItem = null;
        private MapPolyline m_DrawingPolylineItem = null;
        private void DrawPolyKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl && !e.IsRepeat && !m_DrawingPolygon)
            {
                m_DrawingPolygon = true;

                if (m_DrawingPolygonItem != null)
                    m_Map.Children.Remove(m_DrawingPolygonItem);

                m_DrawingPolygonItem = null;
                
                m_DrawingPolylineItem = new MapPolyline();
                m_DrawingPolylineItem.Locations = new LocationCollection();
                m_DrawingPolylineItem.Opacity = 0.7;
                m_DrawingPolylineItem.Stroke = System.Windows.Media.Brushes.Blue;
                m_DrawingPolylineItem.StrokeThickness = 3;

                m_Map.Children.Add(m_DrawingPolylineItem);

                e.Handled = true;
            }

            if (e.Key == Key.RightCtrl && !e.IsRepeat && m_DrawingPolygonItem != null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("double[,] points = new double[,] {");

                for (int i = 0, n= m_DrawingPolygonItem.Locations.Count; i < n; i++)
                {
                    Point p = m_Map.Project(m_DrawingPolygonItem.Locations[i], m_Map.MaxZoomLevel);
                    sb.AppendFormat("{0}{{{1}, {2}}}", (i == 0 ? string.Empty : ", "), p.X, p.Y);
                }

                sb.Append("};");

                Window textBoxWindow = new Window();
                textBoxWindow.Width = 320;
                textBoxWindow.Height = 240;

                TextBox textBox = new TextBox();
                textBox.Text = sb.ToString();
                textBox.TextWrapping = TextWrapping.WrapWithOverflow;
                textBoxWindow.Content = textBox;

                textBoxWindow.Show();
            }
        }
        private void DrawPolyKeyUpHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl && m_DrawingPolygon)
            {
                m_DrawingPolygon = false;

                m_DrawingPolygonItem = new MapPolygon();
                m_DrawingPolygonItem.Locations = m_DrawingPolylineItem.Locations;
                m_DrawingPolygonItem.Opacity = m_DrawingPolylineItem.Opacity;
                m_DrawingPolygonItem.Stroke = m_DrawingPolylineItem.Stroke;
                m_DrawingPolygonItem.StrokeThickness = m_DrawingPolylineItem.StrokeThickness;

                m_Map.Children.Add(m_DrawingPolygonItem);

                m_Map.Children.Remove(m_DrawingPolylineItem);
                m_DrawingPolylineItem = null;

                e.Handled = true;
            }
        }
        private void DrawPolyMouseDownHandler(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed && m_DrawingPolygon && m_DrawingPolylineItem != null)
            {
                m_DrawingPolylineItem.Locations.Add(m_Map.ViewportPointToLocation(e.GetPosition(m_Map)));

                e.Handled = true;
            }
        }
        #endregion
    }
}
