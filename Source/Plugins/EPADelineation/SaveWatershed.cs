﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Services;
using System.Windows.Forms;
using DotSpatial.Controls;
using DotSpatial.Controls.RibbonControls;
using DotSpatial.Data;
using DotSpatial.Projections;
using DotSpatial.Symbology;
using DotSpatial.Topology;
using HydroDesktop.Configuration;
using HydroDesktop.Help;

namespace EPADelineation
{
    public partial class SaveWatershed : Form
    {
        #region Variables

        public static string _wshedpoint = "";
        public static string _wshed = "";
        public static string _stream = "";

        private AppManager _mapArgs;

        private BackgroundWorker _bgw;

        private bool isActive;

        private ProjectionInfo _defaultProjection;

        private ProjectionInfo wgs84 = KnownCoordinateSystems.Geographic.World.WGS1984;

        private readonly string _localHelpUri = Properties.Settings.Default.localHelpUri;

        #endregion Variables

        #region Constructor

        public SaveWatershed(AppManager mapArgs)
        {
            InitializeComponent();
            _mapArgs = mapArgs;

            //Setup background worker
            _bgw = new BackgroundWorker();
            _bgw.WorkerSupportsCancellation = false;
            _bgw.WorkerReportsProgress = false;
            _bgw.DoWork += new DoWorkEventHandler(_bgw_DoWork);
            _bgw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(_bgw_RunWorkerCompleted);
        }

        #endregion Constructor

        #region BackgroundWorker Methods

        private void _bgw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.Message);
            }

            else if (e.Result == null)
            {
                MessageBox.Show("No Watershed Polygon is added.");
            }

            else
            {
                IList<IFeatureSet> result = e.Result as IList<IFeatureSet>;

                //this checks correct type of result
                if (result == null)
                {
                    _mapArgs.Map.Cursor = Cursors.Default;
                    return;
                }
                if (result[0] == null || result[1] == null || result[2] == null)
                {
                    _mapArgs.Map.Cursor = Cursors.Default;
                    return;
                }

                _defaultProjection = _mapArgs.Map.Projection;

                DotSpatial.Projections.GeographicCategories.World world = new DotSpatial.Projections.GeographicCategories.World();
                DotSpatial.Projections.ProjectedCategories.World projWorld = new DotSpatial.Projections.ProjectedCategories.World();

                //This reprojection procedure is critical and important to finally get the correct projection.
                foreach (IFeatureSet fs in result)
                {
                    fs.Projection = world.WGS1984;
                    fs.Reproject(projWorld.WebMercator);
                }

                try
                {
                    AddEPAShapes(result);
                    _mapArgs.Map.Cursor = Cursors.Default;
                    isActive = false;
                }

                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void _bgw_DoWork(object sender, DoWorkEventArgs e)
        {
            object[] param = e.Argument as object[];

            BackgroundWorker worker = sender as BackgroundWorker;

            e.Result = GetShapes(param, worker, e);
        }

        # endregion

        #region Click_Events

        public void OK_Click(object sender, EventArgs e)
        {
            _wshedpoint = this.tbwshedpoint.Text;
            _wshed = this.tbwshed.Text;
            _stream = this.tbstreamline.Text;

            Application.DoEvents();

            string folderpath = Settings.Instance.CurrentProjectDirectory;
            string delineationpath = Path.Combine(folderpath, "Delineation");
            string[] filename = new string[3];
            filename[0] = _wshedpoint + ".shp";
            filename[1] = _stream + ".shp";
            filename[2] = _wshed + ".shp";

            if (!Directory.Exists(delineationpath))
            {
                Directory.CreateDirectory(delineationpath);
            }

            //Specify file names
            for (int i = 0; i < filename.Length; i++)
            {
                string pathi = Path.Combine(delineationpath, filename[i]);

                if ((File.Exists(pathi) == true) && (cbxOverwrite.Checked == false))
                {
                    string message = "File " + pathi + " already exists.\nPlease specify a different name.";

                    MessageBox.Show(message, "Save Watersheds", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

                    return;
                }

                switch (i)
                {
                    case 0:
                        _wshedpoint = pathi;
                        break;
                    case 1:
                        _stream = pathi;
                        break;
                    case 2:
                        _wshed = pathi;
                        break;
                }
            }

            try
            {
                _mapArgs.Map.Cursor = Cursors.Cross;
                ((Map)_mapArgs.Map).MouseClick += new MouseEventHandler(Mouse_Click);
                this.Close();
                isActive = true;
            }

            catch (Exception ex)
            {
                if (ex != null)
                {
                    MessageBox.Show(ex.Message);
                }

                else
                    _mapArgs.Map.Cursor = Cursors.Default;
            }
        }

        public void Cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
            isActive = false;
        }

        private void SaveDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            isActive = false;
        }

        /// <summary>
        /// Opens a help topic for the item in context when the Help button is clicked.
        /// </summary>
        private void SaveDialog_HelpButtonClicked(object sender, CancelEventArgs e)
        {
            LocalHelp.OpenHelpFile(_localHelpUri);
            e.Cancel = true; // Prevents mouse cursor from changing to question mark.
        }

        /// <summary>
        /// Opens a help topic for the item in context when the user presses F1.
        /// </summary>
        private void SaveDialog_HelpRequested(object sender, HelpEventArgs hlpevent)
        {
            LocalHelp.OpenHelpFile(_localHelpUri);
            hlpevent.Handled = true; // Signal that we've handled the help request.
        }

        /// <summary>
        /// Called when Mouse Click occurred on the map
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Mouse_Click(object sender, MouseEventArgs e)
        {
            // Make sure we aren't still working on a previous task
            if (_bgw.IsBusy == true)
            {
                MessageBox.Show("The background worker is busy now. Please try later.");
                return;
            }

            MouseButtons click = e.Button;
            Coordinate projCor = new Coordinate();

            Map _mainMap = _mapArgs.Map as Map;

            _defaultProjection = KnownCoordinateSystems.Projected.World.WebMercator;

            //Must satisfy these three prerequisites to trig the delineation
            if ((click == MouseButtons.Left) && (_mapArgs.Map.Cursor == Cursors.Cross) && isActive)
            {
                try
                {
                    _mapArgs.Map.Cursor = Cursors.WaitCursor;

                    System.Drawing.Point _mouseLocation = new System.Drawing.Point();
                    _mouseLocation.X = e.X;
                    _mouseLocation.Y = e.Y;

                    projCor = _mapArgs.Map.PixelToProj(_mouseLocation);

                    double[] xy = new double[2];
                    xy[0] = projCor.X;
                    xy[1] = projCor.Y;

                    double[] z = new double[1];
                    //Try to project here
                    Reproject.ReprojectPoints(xy, z, _defaultProjection, wgs84, 0, 1);

                    projCor.X = xy[0];
                    projCor.Y = xy[1];

                    object[] parameters = new object[2];

                    parameters[0] = (object)projCor;
                    parameters[1] = (object)_mainMap;

                    if (_bgw.IsBusy != true)
                    {
                        _bgw.RunWorkerAsync(parameters);
                    }
                }

                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        #endregion Click_Events

        #region Methods

        /// <summary>
        /// Call CallWebService Method to get delineated watershed, and also return the start point.
        /// </summary>
        /// <param name="param">Arguments for backgroundworkers</param>
        /// <param name="bgw_worker">Declare a backgroundworker</param>
        /// <param name="e">Do work event</param>
        /// <returns>Return a list of featureset including both point and polygon</returns>
        public IList<IFeatureSet> GetShapes(object[] param, BackgroundWorker bgw_worker, DoWorkEventArgs e)
        {
            Coordinate projCor = (Coordinate)param[0];
            Map _mainMap = (Map)param[1];

            //For Progress report
            FmProgress progress = new FmProgress();
            progress.Show();
            Application.DoEvents();

            //Declare a new CallWebService Client
            CallWebService trigger = new CallWebService(projCor);

            //Get Start Point Information
            object[] startpt = trigger.GetStartPoint();

            //check if start point successful
            if (startpt == null)
            {
                progress.closeForm();
            }

            if (progress._isworking == false)
            {
                progress.updateText(startpt);
            }

            //Get delineated watershed
            object[] WshedObj = new object[2];
            IFeatureSet fsCatchment = new FeatureSet();

            WshedObj = trigger.GetWsheds(startpt);
            IFeatureSet fsWshed = new FeatureSet();

            //Delete small marginal polygons if any
            try
            {
                fsCatchment = WshedObj[0] as IFeatureSet;
                int count = fsCatchment.Features.Count;
                if (count > 1)
                {
                    //The last one is the main watershed
                    for (int i = 0; i < count - 1; i++)
                    {
                        fsCatchment.Features.RemoveAt(0);
                    }

                    //Object process could be dangerous to lose Projection info
                    WshedObj[0] = fsCatchment as object;
                }

                fsWshed = SetAttribute(WshedObj);
            }

            catch (Exception ex)
            {
                // As a bare minimum we should probably log these errors
                System.Diagnostics.Trace.WriteLine(ex.Message);
            }

            //Get Upstream flowlines
            object[] StreamObj = new object[4];
            IFeatureSet fsStream = new FeatureSet();

            StreamObj = trigger.GetLines(startpt);
            fsStream = SetAttribute(StreamObj);

            if (progress._isworking == false)
            {
                progress.updateText();
            }

            //Create the start point shapefile
            Feature point = new Feature(projCor);
            IFeatureSet fsPoint = new FeatureSet();
            fsPoint = new FeatureSet(point.FeatureType);
            fsPoint.AddFeature(point);

            IList<IFeatureSet> EPAShapes = new List<IFeatureSet>();
            EPAShapes.Add(fsWshed);
            EPAShapes.Add(fsStream);
            EPAShapes.Add(fsPoint);

            if (progress._isworking == false)
            {
                try
                {
                    progress.closeForm();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex.Message);
                    //MessageBox.Show(ex.Message);
                    //progress.closeForm();
                    //return;
                }
            }

            return EPAShapes;
        }

        /// <summary>
        /// Created for setting attribute table for shapefiles.
        /// </summary>
        /// <param name="attri">object[] Attributes including necessary information</param>
        /// <returns>Returns the IFeatureSet with attribute table filled</returns>
        public IFeatureSet SetAttribute(object[] attri)
        {
            if (attri == null) return null;

            IFeatureSet Ifs = new FeatureSet();

            Ifs = attri[0] as IFeatureSet;

            FeatureSet fs = Ifs as FeatureSet;

            //Fill Streamlines' attribute table
            if (Ifs.FeatureType == FeatureType.Line)
            {
                List<string> comid = attri[1] as List<string>;
                List<string> reachcode = attri[2] as List<string>;
                List<string> totdist = attri[3] as List<string>;

                DataColumn Id = new DataColumn("Id");
                DataColumn Comid = new DataColumn("Comid");
                DataColumn Reachcode = new DataColumn("ReachCode");
                DataColumn Totdist = new DataColumn("Length(km)");

                fs.DataTable.Columns.Add(Id);
                fs.DataTable.Columns.Add(Comid);
                fs.DataTable.Columns.Add(Reachcode);
                fs.DataTable.Columns.Add(Totdist);

                for (int i = 0; i < fs.Features.Count; i++)
                {
                    fs.Features[i].DataRow["Id"] = (i + 1);
                    fs.Features[i].DataRow["Comid"] = comid[i];
                    fs.Features[i].DataRow["ReachCode"] = reachcode[i];
                    fs.Features[i].DataRow["Length(km)"] = totdist[i];
                }
            }

            else
            {
                string wshedarea = attri[1] as string;

                DataColumn Area = new DataColumn("Area(sq_km)");
                DataColumn Id = new DataColumn("Id");

                fs.DataTable.Columns.Add(Id);
                fs.DataTable.Columns.Add(Area);

                if (fs.Features.Count == 1)
                {
                    fs.Features[0].DataRow["Id"] = 1;
                    fs.Features[0].DataRow["Area(sq_km)"] = wshedarea;
                }

                else
                {
                    int count = fs.Features.Count;

                    try
                    {
                        for (int i = 0; i < count - 1; i++)
                        {
                            fs.Features[i].DataRow.Delete();
                        }
                    }

                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }

                    fs.Features[0].DataRow["Id"] = 1;
                    fs.Features[0].DataRow["Area(sq_km)"] = wshedarea;
                }
            }

            return fs;
        }

        /// <summary>
        /// Add the start point and the delineated polygon into Map and also save them as shapefiles
        /// </summary>
        /// <param name="pointpolygon">IList of IFeatureset saving both start point and delineated polygon</param>
        public void AddEPAShapes(IList<IFeatureSet> pointpolygon)
        {
            if (pointpolygon != null)
            {
                foreach (IFeatureSet fsset in pointpolygon)
                {
                    //Assign projection here <--Necessary
                    fsset.Projection = _mapArgs.Map.Projection;
                    //FeatureSet fs = fsset as FeatureSet;

                    if (fsset.FeatureType == FeatureType.Point)
                    {
                        try
                        {
                            //Save featureset as a MapPointLayer
                            IMapPointLayer point = new MapPointLayer(fsset);
                            point.LegendText = Path.GetFileNameWithoutExtension(_wshedpoint);

                            _mapArgs.Map.Layers.Add(point);

                            fsset.Filename = _wshedpoint;
                            fsset.SaveAs(_wshedpoint, true);
                        }
                        catch (Exception ex)
                        {
                            string message = ex.Message + " /n Failed to add the point.";
                            MessageBox.Show(message);
                        }
                    }

                    if (fsset.FeatureType == FeatureType.Line)
                    {
                        try
                        {
                            //Save featureset as a MapLineLayer
                            IMapLineLayer line = new MapLineLayer(fsset);
                            line.LegendText = Path.GetFileNameWithoutExtension(_stream);

                            LineSymbolizer linesymbol = new LineSymbolizer(Color.Blue, 1);
                            line.Symbolizer = linesymbol;

                            _mapArgs.Map.Layers.Add(line);

                            fsset.Filename = _stream;
                            fsset.Save();
                        }
                        catch (Exception ex)
                        {
                            string message = ex.Message + " /n Failed to add the streamline.";
                            MessageBox.Show(message);
                        }

                        #region Previous Save Code

                        //    string folderpath = Settings.Instance.ApplicationDataDirectory;
                        //    string delineationpath = Path.Combine(folderpath, "Delineation");
                        //    string filename = _stream + ".shp";

                        //    if (!Directory.Exists(delineationpath))
                        //    {
                        //        Directory.CreateDirectory(delineationpath);
                        //    }

                        //    fsset.Filename = Path.Combine(delineationpath, filename);

                        //    if (File.Exists(fsset.Filename) == true)
                        //    {
                        //        string message = "File " + fsset.Filename + " already exists.\nWould you like to replace it?";

                        //        DialogResult replace = MessageBox.Show(message, "Save Watersheds", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        //        if (replace == DialogResult.No)
                        //            return;
                        //        else
                        //        {
                        //            File.Delete(fsset.Filename);
                        //            fsset.Save();
                        //        }
                        //    }
                        # endregion
                    }

                    if (fsset.FeatureType == FeatureType.Polygon)
                    {
                        #region Previous Projection code

                        // Different methods to display polygon/multipolygon features on the Map
                        // This first method has problems in displaying multipolygon features
                        //if (fsset.Features.Count == 1)
                        //{
                        //    //fsset.Reproject(_mapArgs.Map.Projection);
                        //    IMapPolygonLayer polyfl = new MapPolygonLayer(fsset);

                        //    polyfl.LegendText = _wshed;

                        //    _mapArgs.Map.Layers.Add(polyfl);

                        //    string folderpath = Settings.Instance.ApplicationDataDirectory;
                        //    string delineationpath = Path.Combine(folderpath, "Delineation");
                        //    string filename = _wshed + ".shp";

                        //    if (!Directory.Exists(delineationpath))
                        //    {
                        //        Directory.CreateDirectory(delineationpath);
                        //    }

                        //    fsset.Filename = Path.Combine(delineationpath, filename);
                        //    fsset.Save();
                        //}

                        //// This second method sometimes still rises projection problem
                        //else if (fsset.Features.Count > 1)
                        //{

                        #endregion Previous Projection code

                        try
                        {
                            //Effective in solving projection problem to display polygon
                            string file = _wshed;
                            //fsset.Reproject(_mapArgs.Map.Projection);
                            fsset.SaveAs(file, true);

                            IFeatureSet polyfs = FeatureSet.Open(file);

                            polyfs.Projection = KnownCoordinateSystems.Projected.World.WebMercator;
                            polyfs.Reproject(_mapArgs.Map.Projection);

                            PolygonSymbolizer polysymbol = new PolygonSymbolizer(Color.LightBlue.ToTransparent((float)0.7), Color.DarkBlue);

                            IMapPolygonLayer poly = new MapPolygonLayer(polyfs);
                            poly.Symbolizer = polysymbol;

                            _mapArgs.Map.Layers.Add(poly);
                        }
                        catch (Exception ex)
                        {
                            string message = ex.Message + " /n Failed to add the watershed.";
                            MessageBox.Show(message);
                        }
                    }
                }
            }
        }

        # endregion
    }
}