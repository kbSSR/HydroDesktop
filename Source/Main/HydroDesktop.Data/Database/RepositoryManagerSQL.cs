﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using HydroDesktop.Interfaces.ObjectModel;
using System.Windows.Forms;
using System.Data.Common;
using System.ComponentModel;
using HydroDesktop.Interfaces;

namespace HydroDesktop.Database
{
    public class RepositoryManagerSQL : IRepositoryManager
    {
        #region Variables

        // The helper class wraps the database operations
        private DbOperations _db;

        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new instance of the manager given a connection string
        /// </summary>
        /// <param name="dbType">The type of the database (SQLite, SQLServer, ...)</param>
        /// <param name="connectionString">The connection string</param>
        public RepositoryManagerSQL(DatabaseTypes dbType, string connectionString)
        {
            //if it's a SQLite database - check if DB file exists
            if (dbType == DatabaseTypes.SQLite)
            {
                CheckDbFile(connectionString);
            }
            
            //initialize the DAO objects           
            _db = new DbOperations(connectionString, dbType);

            
        }

        /// <summary>
        /// Creates a new RepositoryManager associated with the specified database
        /// </summary>
        /// <param name="db">The DbOperations object for handling the database</param>
        public RepositoryManagerSQL(DbOperations db)
        {
            //if it's a SQLite database - check if DB file exists
            if (db.DatabaseType == DatabaseTypes.SQLite)
            {
                CheckDbFile(db.ConnectionString);
            }

            _db = db;
        }
        #endregion

        #region Properties

        public IHydroDbOperations DbOperations
        {
            get { return _db; }
        }

        #endregion

        #region Public Methods

        #region Delete Series or Theme
        /// <summary>
        /// Deletes a theme and all its series as long as the series don't belong to any other theme.
        /// </summary>
        /// <param name="themeID">The Theme ID</param>
        /// <returns>true if successful, false otherwise</returns>
        public bool DeleteTheme(int themeID)
        {
            string sqlTheme = "SELECT SeriesID FROM DataThemes where ThemeID = " + themeID;
            DataTable tblSeries = _db.LoadTable("tblSeries", sqlTheme);

            int numSeries = tblSeries.Rows.Count;

            foreach (DataRow seriesRow in tblSeries.Rows)
            {
                int seriesID = Convert.ToInt32(seriesRow["SeriesID"]);

                DeleteSeries(seriesID);
            }

            //delete the actual theme
            string sqlDeleteTheme = "DELETE FROM DataThemeDescriptions WHERE ThemeID = " + themeID;
            try
            {
                _db.ExecuteNonQuery(sqlDeleteTheme);
            }
            catch { };

            //re-check the number of series in the theme

            //theme deleted event
            OnThemeDeleted();
            
            return true;
        }

        /// <summary>
        /// Delete a theme - a background worker and progress bar is used
        /// </summary>
        /// <param name="themeID">The themeID (this needs to be a valid ID)</param>
        /// <param name="worker">The background worker component</param>
        /// <param name="e">The arguments for background worker</param>
        /// <returns></returns>
        public bool DeleteTheme(int themeID, BackgroundWorker worker, DoWorkEventArgs e)
        {
            string sqlTheme = "SELECT SeriesID FROM DataThemes where ThemeID = " + themeID;
            DataTable tblSeries = _db.LoadTable("tblSeries", sqlTheme);

            int numSeries = tblSeries.Rows.Count;
            int count = 0;

            if (numSeries == 0)
            {
                return false;
            }

            foreach (DataRow seriesRow in tblSeries.Rows)
            {
                if (worker != null)
                {
                    //check cancellation
                    if (e != null && worker.CancellationPending)
                    {
                        e.Cancel = true;
                        return false;
                    }
                }
                
                int seriesID = Convert.ToInt32(seriesRow["SeriesID"]);

                DeleteSeries(seriesID);

                //progress report
                count++;

                if (worker != null && worker.WorkerReportsProgress)
                {
                    int percent = (int)(((float)count / (float)numSeries) * 100);
                    string userState = "Deleting series " + count + " of " + numSeries + "...";
                    worker.ReportProgress(percent, userState);
                }
            }

            //delete the actual theme

            string sqlDeleteTheme = "DELETE FROM DataThemeDescriptions WHERE ThemeID = " + themeID;
            try
            {
                _db.ExecuteNonQuery(sqlDeleteTheme);
                e.Result = "Theme deleted successfully";
            }
            catch { };

            return true;
        }
        
        
        /// <summary>
        /// Deletes a series given it's ID. The series is only deleted when it belongs to one theme.
        /// </summary>
        /// <param name="seriesID">The database ID of the series</param>
        /// <returns>true if series was deleted, false otherwise</returns>
        public bool DeleteSeries(int seriesID)
        {
            string sqlTheme =
                "SELECT ThemeID from DataThemes where SeriesID = " + seriesID;
            DataTable tblTheme = _db.LoadTable("tblTheme", sqlTheme);

            //if the series belongs to multiple themes, do not delete it.
            if (tblTheme.Rows.Count != 1) return false;

            //otherwise, delete the series
            Series seriesToDel = GetSeriesByID(seriesID);

            //SQL Queries
            string sqlSite = "SELECT SiteID from DataSeries where SiteID = " + seriesToDel.Site.Id;
            string sqlVariable = "SELECT VariableID from DataSeries where VariableID = " + seriesToDel.Variable.Id;
            string sqlSource = "SELECT SourceID from DataSeries where SourceID = " + seriesToDel.Source.Id;
            string sqlMethod = "SELECT MethodID from DataSeries where MethodID = " + seriesToDel.Method.Id;
            string sqlQuality = "SELECT QualityControlLevelID from DataSeries where QualityControlLevelID = " + seriesToDel.QualityControlLevel.Id;


            //SQL Delete Commands
            string sqlDeleteValues = "DELETE FROM DataValues WHERE SeriesID = " + seriesID;
            string sqlDeleteSeries = "DELETE FROM DataSeries WHERE SeriesID = " + seriesID;
            string sqlDeleteSeriesTheme = "DELETE FROM DataThemes WHERE SeriesID = " + seriesID;

            string sqlDeleteSite = "DELETE FROM Sites WHERE SiteID = " + seriesToDel.Site.Id;
            string sqlDeleteVariable = "DELETE FROM Variables WHERE VariableID = " + seriesToDel.Variable.Id;
            string sqlDeleteMethod = "DELETE FROM Methods WHERE MethodID = " + seriesToDel.Method.Id;
            string sqlDeleteSource = "DELETE FROM Sources WHERE SourceID = " + seriesToDel.Source.Id;
            string sqlDeleteQuality = "DELETE FROM QualityControlLevels WHERE QualityControlLevelID = " + seriesToDel.QualityControlLevel.Id;
            string sqlDeleteSelection = "DELETE FROM Selection WHERE SeriesID = " + seriesID;

            DataTable tblSite = new DataTable();
            DataTable tblVariable = new DataTable();
            DataTable tblSource = new DataTable();
            DataTable tblMethod = new DataTable();
            DataTable tblQuality = new DataTable();

            //Begin Transaction
            using (DbConnection conn = _db.CreateConnection())
            {
                conn.Open();

                using (DbTransaction tran = conn.BeginTransaction())
                {
                    // get site IDs
                    using (DbCommand cmd01 = conn.CreateCommand())
                    {
                        cmd01.CommandText = sqlSite;
                        tblSite = _db.LoadTable("t1", cmd01);
                    }

                    // get variable IDs
                    using (DbCommand cmd02 = conn.CreateCommand())
                    {
                        cmd02.CommandText = sqlVariable;
                        tblVariable = _db.LoadTable("t2", cmd02);
                    }

                    // get source IDs
                    using (DbCommand cmd03 = conn.CreateCommand())
                    {
                        cmd03.CommandText = sqlSource;
                        tblSource = _db.LoadTable("t3", cmd03);
                    }
                    
                    // get method IDs
                    using (DbCommand cmd04 = conn.CreateCommand())
                    {
                        cmd04.CommandText = sqlMethod;
                        tblMethod = _db.LoadTable("t4", cmd04);
                    }

                    // get qualityControl IDs
                    using (DbCommand cmd05 = conn.CreateCommand())
                    {
                        cmd05.CommandText = sqlQuality;
                        tblQuality = _db.LoadTable("t5", cmd05);
                    }

                    //delete the site
                    if (tblSite.Rows.Count == 1)
                    {
                        using (DbCommand cmdDeleteSite = conn.CreateCommand())
                        {
                            cmdDeleteSite.CommandText = sqlDeleteSite;
                            cmdDeleteSite.ExecuteNonQuery(); 
                        }
                    }

                    //delete the variable
                    if (tblVariable.Rows.Count == 1)
                    {
                        using (DbCommand cmdDeleteVariable = conn.CreateCommand())
                        {
                            cmdDeleteVariable.CommandText = sqlDeleteVariable;
                            cmdDeleteVariable.ExecuteNonQuery();
                        }
                    }

                    //delete the method
                    if (tblMethod.Rows.Count == 1)
                    {
                        using (DbCommand cmdDeleteMethod = conn.CreateCommand())
                        {
                            cmdDeleteMethod.CommandText = sqlDeleteMethod;
                            cmdDeleteMethod.ExecuteNonQuery();
                        }
                    }

                    //delete the source
                    if (tblSource.Rows.Count == 1)
                    {
                        using (DbCommand cmdDeleteSource = conn.CreateCommand())
                        {
                            cmdDeleteSource.CommandText = sqlDeleteSource;
                            cmdDeleteSource.ExecuteNonQuery();
                        }
                    }

                    //delete the quality control level
                    if (tblQuality.Rows.Count == 1)
                    {
                        using (DbCommand cmdDeleteQuality = conn.CreateCommand())
                        {
                            cmdDeleteQuality.CommandText = sqlDeleteQuality;
                            cmdDeleteQuality.ExecuteNonQuery();
                        }
                    }

                    //delete the data values
                    using (DbCommand cmdDeleteValues = conn.CreateCommand())
                    {
                        cmdDeleteValues.CommandText = sqlDeleteValues;
                        cmdDeleteValues.ExecuteNonQuery();
                    }

                    //finally delete the series
                    using (DbCommand cmdDeleteSeries = conn.CreateCommand())
                    {
                        cmdDeleteSeries.CommandText = sqlDeleteSeries;
                        cmdDeleteSeries.ExecuteNonQuery();
                    }
                    using (DbCommand cmdDeleteSeriesTheme = conn.CreateCommand())
                    {
                        cmdDeleteSeriesTheme.CommandText = sqlDeleteSeriesTheme;
                        cmdDeleteSeriesTheme.ExecuteNonQuery();
                    }

                    //commit transaction
                    tran.Commit();
                }
            }

            //remove seriesID from 'Selection'
            string dele2 = "DELETE from Selection WHERE SeriesID=" + seriesID;
            try
            {
                _db.ExecuteNonQuery(dele2);
            }
            catch { }
            return true;
        }
        #endregion

        #region SQL Queries
        private string DetailedSeriesSQLQuery()
        {
            string sql = "SELECT DataThemes.SeriesID, " +
                "SiteName, SiteCode, Latitude, Longitude, " +
                "VariableName, VariableCode, DataType, ValueType, Speciation, SampleMedium, " +
                "TimeSupport, GeneralCategory, NoDataValue, " +
                "units1.UnitsName as 'VariableUnitsName', units2.UnitsName as 'TimeUnitsName', " +
                "MethodDescription, " +
                "SourceDescription, Organization, Citation, " +
                "QualityControlLevelCode, Definition as 'QualityControlLevelDefinition', " +
                "BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, ThemeName " +
                "FROM DataThemeDescriptions " +
                "INNER JOIN DataThemes ON DataThemes.ThemeID = DataThemeDescriptions.ThemeID " +
                "INNER JOIN DataSeries ON DataThemes.SeriesID = DataSeries.SeriesID " +
                "INNER JOIN Sites ON DataSeries.SiteID = Sites.SiteID " +
                "INNER JOIN Variables ON DataSeries.VariableID = Variables.VariableID " +
                "INNER JOIN Units units1 ON Variables.VariableUnitsID = units1.UnitsID " +
                "INNER JOIN Units units2 ON Variables.TimeUnitsID = units2.UnitsID " +
                "INNER JOIN Methods  ON DataSeries.MethodID = Methods.MethodID " +
                "INNER JOIN Sources ON DataSeries.SourceID = Sources.SourceID " +
                "INNER JOIN QualityControlLevels ON DataSeries.QualityControlLevelID = QualityControlLevels.QualityControlLevelID ";
            return sql;
        }

        private string DetailedSeriesSQLQuery2()
        {
            string sql = "SELECT DataSeries.SeriesID, " +
                "DataThemes.ThemeID, DataSeries.SiteID, DataSeries.VariableID, DataSeries.MethodID, DataSeries.SourceID, DataSeries.QualityControlLevelID, " +
                "SiteName, SiteCode, Latitude, Longitude, " +
                "VariableName, VariableCode, DataType, ValueType, Speciation, SampleMedium, " +
                "TimeSupport, GeneralCategory, NoDataValue, " +
                "units1.UnitsName as 'VariableUnitsName', units2.UnitsName as 'TimeUnitsName', " +
                "MethodDescription, " +
                "SourceDescription, Organization, Citation, " +
                "QualityControlLevelCode, Definition as 'QualityControlLevelDefinition', " +
                "BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, ThemeName " +
                "FROM DataThemeDescriptions " +
                "INNER JOIN DataThemes ON DataThemes.ThemeID = DataThemeDescriptions.ThemeID " +
                "INNER JOIN DataSeries ON DataThemes.SeriesID = DataSeries.SeriesID " +
                "INNER JOIN Sites ON DataSeries.SiteID = Sites.SiteID " +
                "INNER JOIN Variables ON DataSeries.VariableID = Variables.VariableID " +
                "INNER JOIN Units units1 ON Variables.VariableUnitsID = units1.UnitsID " +
                "INNER JOIN Units units2 ON Variables.TimeUnitsID = units2.UnitsID " +
                "INNER JOIN Methods  ON DataSeries.MethodID = Methods.MethodID " +
                "INNER JOIN Sources ON DataSeries.SourceID = Sources.SourceID " +
                "INNER JOIN QualityControlLevels ON DataSeries.QualityControlLevelID = QualityControlLevels.QualityControlLevelID ";
            return sql;
        }
        
        /// <summary>
        /// Gets a list of all available series in the form of a data table
        /// </summary>
        /// <returns></returns>
        public DataTable GetSeriesListTable()
        {
            string sql = DetailedSeriesSQLQuery();

            DataTable table = _db.LoadTable("SeriesListTable", sql);
            return table;
        }

        /// <summary>
        /// Returns a detailed data table for all series in the database
        /// </summary>
        /// <returns></returns>
        public DataTable GetSeriesTable()
        {
            return GetSeriesListTable();
        }

        /// <summary>
        /// Returns a detailed data table for all series in the database. The
        /// table includes the IDs of site, variable, source, method and QCLevel.
        /// </summary>
        /// <returns></returns>
        public DataTable GetSeriesTable2()
        {
            string sql = DetailedSeriesSQLQuery2();
            sql += " ORDER BY VariableName, SiteName";
            DataTable table = _db.LoadTable("SeriesListTable", sql);
            return table;
        }

        /// <summary>
        /// Returns a detailed table for all series that match the attributes
        /// </summary>
        /// <param name="seriesIDs">The list of series IDs</param>
        /// <returns>the data table</returns>
        public DataTable GetSeriesTable(int[] seriesIDs)
        {
            StringBuilder sqlIn = new StringBuilder();
            for(int i = 0; i< seriesIDs.Length; i++)
            {
                sqlIn.Append(seriesIDs[i].ToString());
                if (i < seriesIDs.Length - 1)
                {
                    sqlIn.Append(",");
                }
            }

            string sql = DetailedSeriesSQLQuery() +
                " WHERE DataThemes.SeriesID in (" + sqlIn.ToString() + ")";

            DataTable table = _db.LoadTable("SeriesListTable", sql);
            return table;
        }

        /// <summary>
        /// Returns a detailed table for all series that match the attributes
        /// </summary>
        /// <param name="seriesIDs">The list of series IDs</param>
        /// <returns>the data table</returns>
        public DataTable GetSeriesTable(IEnumerable<int> listOfSeriesID)
        {
            return GetSeriesTable(listOfSeriesID.ToArray<int>());
        }

        /// <summary>
        /// Returns the data table of detailed properties for one series
        /// </summary>
        /// <param name="seriesID">The id of the series</param>
        /// <returns>The detailed table. This table only has one data row.</returns>
        public DataTable GetSeriesTable(int seriesID)
        {
            string sql = DetailedSeriesSQLQuery() + " WHERE DataSeries.SeriesID=" + seriesID.ToString();

            DataTable table = _db.LoadTable("SeriesListTable", sql);
            return table;
        }
        
        public Series GetSeriesByID(int seriesID)
        {
            Series series = new Series();
            DataTable seriesTable = _db.LoadTable("seriesTable", "select * from DataSeries where seriesID=" + seriesID);
            if (seriesTable.Rows.Count == 0) return null;

            DataRow seriesRow = seriesTable.Rows[0];
            series.BeginDateTime = Convert.ToDateTime(seriesRow["BeginDateTime"]);
            series.EndDateTime = Convert.ToDateTime(seriesRow["EndDateTime"]);
            series.BeginDateTimeUTC = Convert.ToDateTime(seriesRow["BeginDateTimeUTC"]);
            series.EndDateTimeUTC = Convert.ToDateTime(seriesRow["EndDatetimeUTC"]);
            series.Id = seriesID;
            series.IsCategorical = Convert.ToBoolean(seriesRow["IsCategorical"]);
            series.LastCheckedDateTime = Convert.ToDateTime(seriesRow["LastCheckedDateTime"]);
            series.UpdateDateTime = Convert.ToDateTime(seriesRow["UpdateDateTime"]);
            series.Subscribed = Convert.ToBoolean(seriesRow["Subscribed"]);
            series.ValueCount = Convert.ToInt32(seriesRow["ValueCount"]);
            
            int siteID = Convert.ToInt32(seriesRow["SiteID"]);
            
            string sqlSites = "SELECT SiteID, SiteCode, SiteName, Latitude, Longitude, Elevation_m, " +
                "VerticalDatum, LocalX, LocalY, State, County, Comments FROM Sites where SiteID = " + siteID;

            DataTable siteTable = _db.LoadTable("siteTable", sqlSites);
            if (siteTable.Rows.Count == 0) return null;

            DataRow siteRow = siteTable.Rows[0];
            Site newSite = new Site();
            newSite.Id = Convert.ToInt32(siteRow[0]);
            newSite.Code = Convert.ToString(siteRow[1]);
            newSite.Name = Convert.ToString(siteRow[2]);
            newSite.Latitude = Convert.ToDouble(siteRow[3]);
            newSite.Longitude = Convert.ToDouble(siteRow[4]);
            newSite.Elevation_m = Convert.ToDouble(siteRow[5]);
            newSite.VerticalDatum = Convert.ToString(siteRow[6]);
            newSite.LocalX = Convert.ToDouble(siteRow["LocalX"]);
            newSite.LocalY = Convert.ToDouble(siteRow["LocalY"]);
            series.Site = newSite;

            int variableID = Convert.ToInt32(seriesRow["VariableID"]);
            string sqlVariables = "SELECT VariableID, VariableCode, VariableName FROM Variables where VariableID = " + variableID;
            DataTable tblVar = _db.LoadTable("variableTable", sqlVariables);
            if (tblVar.Rows.Count == 0) return null;

            DataRow variableRow = tblVar.Rows[0];
            Variable newVar = new Variable();
            newVar.Id = Convert.ToInt32(variableRow[0]);
            newVar.Code = Convert.ToString(variableRow[1]);
            newVar.Name = Convert.ToString(variableRow[2]);
            series.Variable = newVar;

            Method newMethod = new Method();
            newMethod.Id = Convert.ToInt32(seriesRow["MethodID"]);
            series.Method = newMethod;

            Source newSource = new Source();
            newSource.Id = Convert.ToInt32(seriesRow["SourceID"]);
            series.Source = newSource;

            QualityControlLevel newQC = new QualityControlLevel();
            newQC.Id = Convert.ToInt32(seriesRow["QualityControlLevelID"]);
            series.QualityControlLevel = newQC;


            return series;
        }
        #endregion

        #region Save Series
        /// <summary>
        /// Adds an existing series to an existing theme
        /// </summary>
        /// <param name="series"></param>
        /// <param name="theme"></param>
        public void AddSeriesToTheme(Series series, Theme theme)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Simplified version of SaveSeries (for HydroForecaster)
        /// </summary>
        /// <param name="siteID"></param>
        /// <param name="variableID"></param>
        /// <param name="methodName"></param>
        /// <param name="themeName"></param>
        /// <param name="dataValues">The table with data values. First column must be DateTime and second column must be Double.</param>
        /// <returns></returns>
        public int SaveSeries(int siteID, int variableID, string methodDescription, string themeName, DataTable dataValues)
        { 
            string sqlUnits = "SELECT UnitsID FROM Units WHERE UnitsName = ? AND UnitsType = ? AND UnitsAbbreviation = ?";
            string sqlMethod = "SELECT MethodID FROM Methods WHERE MethodDescription = ?";
            string sqlSource = "SELECT SourceID FROM Sources WHERE Organization = ?";
            string sqlISOMetadata = "SELECT MetadataID FROM ISOMetadata WHERE Title = ? AND MetadataLink = ?";
            string sqlQuality = "SELECT QualityControlLevelID FROM QualityControlLevels WHERE Definition = ?";
            string sqlQualifier = "SELECT QualifierID FROM Qualifiers WHERE QualifierCode = ?";
            string sqlSample = "SELECT SampleID FROM Samples WHERE SampleType = ? AND LabSampleCode = ?";
            string sqlLabMethod = "SELECT LabMethodID FROM LabMethods WHERE LabName = ? AND LabMethodName = ?";
            string sqlOffsetType = "SELECT OffsetTypeID FROM OffsetTypes WHERE OffsetDescription = ?";
            string sqlTheme = "SELECT ThemeID FROM DataThemeDescriptions WHERE ThemeName = ?";
            string sqlRowID = "; SELECT LAST_INSERT_ROWID();";

            string sqlSaveSpatialReference = "INSERT INTO SpatialReferences(SRSID, SRSName) VALUES(?, ?)" + sqlRowID;

            string sqlSaveSite = "INSERT INTO Sites(SiteCode, SiteName, Latitude, Longitude, LatLongDatumID, Elevation_m, VerticalDatum, " +
                                                   "LocalX, LocalY, LocalProjectionID, PosAccuracy_m, State, County, Comments) " +
                                                   "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveUnits = "INSERT INTO Units(UnitsName, UnitsType, UnitsAbbreviation) VALUES(?, ?, ?)" + sqlRowID;

            string sqlSaveVariable = "INSERT INTO Variables(VariableCode, VariableName, Speciation, VariableUnitsID, SampleMedium, ValueType, " +
                "IsRegular, ISCategorical, TimeSupport, TimeUnitsID, DataType, GeneralCategory, NoDataValue) " +
                "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveMethod = "INSERT INTO Methods(MethodDescription, MethodLink) VALUES(?, ?)" + sqlRowID;

            string sqlSaveQualityControl = "INSERT INTO QualityControlLevels(QualityControlLevelCode, Definition, Explanation) " +
                "VALUES(?,?,?)" + sqlRowID;

            string sqlSaveSource = "INSERT INTO Sources(Organization, SourceDescription, SourceLink, ContactName, Phone, " +
                                   "Email, Address, City, State, ZipCode, Citation, MetadataID) " +
                                   "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveISOMetadata = "INSERT INTO ISOMetadata(TopicCategory, Title, Abstract, ProfileVersion, MetadataLink) " +
                                    "VALUES(?,?,?,?,?)" + sqlRowID;

            string sqlSaveSeries = "INSERT INTO DataSeries(SiteID, VariableID, MethodID, SourceID, QualityControlLevelID, " +
                "IsCategorical, BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, CreationDateTime, " +
                "Subscribed, UpdateDateTime, LastCheckedDateTime) " +
                "VALUES(?, ?, ?, ?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveQualifier = "INSERT INTO Qualifiers(QualifierCode, QualifierDescription) VALUES (?,?)" + sqlRowID;

            string sqlSaveSample = "INSERT INTO Samples(SampleType, LabSampleCode, LabMethodID) VALUES (?,?, ?)" + sqlRowID;

            string sqlSaveLabMethod = "INSERT INTO LabMethods(LabName, LabOrganization, LabMethodName, LabMethodLink, LabMethodDescription) " +
                "VALUES(?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveOffsetType = "INSERT INTO OffsetTypes(OffsetUnitsID, OffsetDescription) VALUES (?, ?)" + sqlRowID;

            string sqlSaveDataValue = "INSERT INTO DataValues(SeriesID, DataValue, ValueAccuracy, LocalDateTime, " +
                "UTCOffset, DateTimeUTC, OffsetValue, OffsetTypeID, CensorCode, QualifierID, SampleID, FileID) " +
                "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)";

            string sqlSaveTheme1 = "INSERT INTO DataThemeDescriptions(ThemeName, ThemeDescription) VALUES (?,?)" + sqlRowID;
            string sqlSaveTheme2 = "INSERT INTO DataThemes(ThemeID,SeriesID) VALUEs (?,?)";

            //int spatialReferenceID = 0;
            //int localProjectionID = 0;
            //int variableUnitsID = 0;
            //int timeUnitsID = 0;
            int methodID = 0;
            int qualityControlLevelID = 0;
            int sourceID = 0;
            int isoMetadataID = 0;
            int seriesID = 0;
            int themeID = 0;
            
            //object siteIDResult = null;
            //object spatialReferenceIDResult = null;
            //object localProjectionIDResult = null;
            //object variableIDResult = null;
            //object variableUnitsIDResult = null;
            //object timeUnitsIDResult = null;
            object methodIDResult = null;
            object qualityControlLevelIDResult = null;
            object sourceIDResult = null;
            object isoMetadataIDResult = null;
            object seriesIDResult = null;
            object qualifierIDResult = null;
            object themeIDResult = null;
            object sampleIDResult = null;
            object labMethodIDResult = null;
            object offsetTypeIDResult = null;
            object offsetUnitIDResult = null;

            Dictionary<string, Qualifier> qualifierLookup = new Dictionary<string, Qualifier>();
            Dictionary<string, Sample> sampleLookup = new Dictionary<string, Sample>();
            Dictionary<string, OffsetType> offsetLookup = new Dictionary<string, OffsetType>();

            //create the series object
            Series series = new Series();
            series.Variable = new Variable();
            series.Method = Method.Unknown;
            series.Method.Description = methodDescription;

            //to add the data values
            foreach (DataRow row in dataValues.Rows)
            {
                series.AddDataValue(Convert.ToDateTime(row[0]), Convert.ToDouble(row[1]));
            }

            Theme theme = new Theme(themeName);
            
            int numSavedValues = 0;

            //Step 1 Begin Transaction
            using (DbConnection conn = _db.CreateConnection())
            {
                conn.Open();

                using (DbTransaction tran = conn.BeginTransaction())
                {
                    //****************************************************************
                    //*** Step 4 Method
                    //****************************************************************
                    Method method = series.Method;

                    using (DbCommand cmd10 = conn.CreateCommand())
                    {
                        cmd10.CommandText = sqlMethod;
                        cmd10.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                        methodIDResult = cmd10.ExecuteScalar();
                        if (methodIDResult != null)
                        {
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    if (methodID == 0)
                    {
                        using (DbCommand cmd11 = conn.CreateCommand())
                        {
                            cmd11.CommandText = sqlSaveMethod;
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Link));
                            methodIDResult = cmd11.ExecuteScalar();
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 5 Quality Control Level
                    //****************************************************************
                    QualityControlLevel qc = series.QualityControlLevel;

                    using (DbCommand cmd12 = conn.CreateCommand())
                    {
                        cmd12.CommandText = sqlQuality;
                        cmd12.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                        qualityControlLevelIDResult = cmd12.ExecuteScalar();
                        if (qualityControlLevelIDResult != null)
                        {
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    if (qualityControlLevelID == 0)
                    {
                        using (DbCommand cmd13 = conn.CreateCommand())
                        {
                            cmd13.CommandText = sqlSaveQualityControl;
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Code));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Explanation));
                            qualityControlLevelIDResult = cmd13.ExecuteScalar();
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 6 Source
                    //****************************************************************
                    Source source = series.Source;

                    using (DbCommand cmd14 = conn.CreateCommand())
                    {
                        cmd14.CommandText = sqlSource;
                        cmd14.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                        sourceIDResult = cmd14.ExecuteScalar();
                        if (sourceIDResult != null)
                        {
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    if (sourceID == 0)
                    {
                        ISOMetadata isoMetadata = source.ISOMetadata;

                        using (DbCommand cmd15 = conn.CreateCommand())
                        {
                            cmd15.CommandText = sqlISOMetadata;
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                            isoMetadataIDResult = cmd15.ExecuteScalar();
                            if (isoMetadataIDResult != null)
                            {
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        if (isoMetadataID == 0)
                        {
                            using (DbCommand cmd16 = conn.CreateCommand())
                            {
                                cmd16.CommandText = sqlSaveISOMetadata;
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.TopicCategory));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Abstract));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.ProfileVersion));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                                isoMetadataIDResult = cmd16.ExecuteScalar();
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        using (DbCommand cmd17 = conn.CreateCommand())
                        {
                            cmd17.CommandText = sqlSaveSource;
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Description));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Link));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.ContactName));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Phone));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Email));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Address));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.City));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.State));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.Int32, source.ZipCode));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Citation));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadataID));
                            sourceIDResult = cmd17.ExecuteScalar();
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 7 Series
                    //****************************************************************
                    using (DbCommand cmd18 = conn.CreateCommand())
                    {
                        cmd18.CommandText = sqlSaveSeries;
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.IsCategorical));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTimeUTC));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTimeUTC));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, series.ValueCount));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.CreationDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.Subscribed));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.UpdateDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.LastCheckedDateTime));

                        seriesIDResult = cmd18.ExecuteScalar();
                        seriesID = Convert.ToInt32(seriesIDResult);
                    }


                    //****************************************************************
                    //*** Step 8 Qualifier and Sample Lookup
                    //****************************************************************
                    foreach (DataValue val in series.DataValueList)
                    {
                        if (val.Qualifier != null)
                        {
                            if (!qualifierLookup.ContainsKey(val.Qualifier.Code))
                            {
                                qualifierLookup.Add(val.Qualifier.Code, val.Qualifier);
                            }
                        }

                        if (val.Sample != null)
                        {
                            if (!sampleLookup.ContainsKey(val.Sample.LabSampleCode))
                            {
                                sampleLookup.Add(val.Sample.LabSampleCode, val.Sample);
                            }
                        }
                        if (val.OffsetType != null)
                        {
                            if (!offsetLookup.ContainsKey(val.OffsetType.Description))
                            {
                                offsetLookup.Add(val.OffsetType.Description, val.OffsetType);
                            }
                        }
                    }

                    //****************************************************************
                    //*** Step 9 Qualifiers
                    //****************************************************************
                    if (qualifierLookup.Count > 0)
                    {
                        using (DbCommand cmd19 = conn.CreateCommand())
                        {
                            cmd19.CommandText = sqlQualifier;
                            cmd19.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Qualifier qualifier in qualifierLookup.Values)
                            {
                                cmd19.Parameters[0].Value = qualifier.Code;
                                qualifierIDResult = cmd19.ExecuteScalar();
                                if (qualifierIDResult != null)
                                {
                                    qualifier.Id = Convert.ToInt32(qualifierIDResult);
                                }
                            }
                        }

                        List<Qualifier> unsavedQualifiers = new List<Qualifier>();
                        foreach (Qualifier qual in qualifierLookup.Values)
                        {
                            if (qual.Id == 0)
                            {
                                unsavedQualifiers.Add(qual);
                            }
                        }

                        if (unsavedQualifiers.Count > 0)
                        {
                            using (DbCommand cmd20 = conn.CreateCommand())
                            {
                                cmd20.CommandText = sqlSaveQualifier;
                                cmd20.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd20.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Qualifier qual2 in unsavedQualifiers)
                                {
                                    cmd20.Parameters[0].Value = qual2.Code;
                                    cmd20.Parameters[1].Value = qual2.Description;
                                    qualifierIDResult = cmd20.ExecuteScalar();
                                    qual2.Id = Convert.ToInt32(qualifierIDResult);
                                }
                            }
                        }
                    }

                    //****************************************************************
                    //*** TODO Step 10 Samples and Lab Methods
                    //****************************************************************
                    if (sampleLookup.Count > 0)
                    {
                        Dictionary<string, LabMethod> labMethodLookup = new Dictionary<string, LabMethod>();

                        using (DbCommand cmd21 = conn.CreateCommand())
                        {
                            cmd21.CommandText = sqlSample;
                            cmd21.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd21.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Sample sample in sampleLookup.Values)
                            {
                                cmd21.Parameters[0].Value = sample.SampleType;
                                cmd21.Parameters[1].Value = sample.LabSampleCode;
                                sampleIDResult = cmd21.ExecuteScalar();
                                if (sampleIDResult != null)
                                {
                                    sample.Id = Convert.ToInt32(sampleIDResult);
                                }
                            }
                        }


                        List<Sample> unsavedSamples = new List<Sample>();
                        List<LabMethod> unsavedLabMethods = new List<LabMethod>();

                        foreach (Sample samp in sampleLookup.Values)
                        {
                            if (samp.Id == 0)
                            {
                                unsavedSamples.Add(samp);
                                string labMethodKey = samp.LabMethod.LabName + "|" + samp.LabMethod.LabMethodName;
                                if (!labMethodLookup.ContainsKey(labMethodKey))
                                {
                                    labMethodLookup.Add(labMethodKey, samp.LabMethod);
                                }
                            }
                        }

                        using (DbCommand cmd22 = conn.CreateCommand())
                        {
                            cmd22.CommandText = sqlLabMethod;
                            cmd22.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd22.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (LabMethod labMethod in labMethodLookup.Values)
                            {
                                cmd22.Parameters[0].Value = labMethod.LabName;
                                cmd22.Parameters[1].Value = labMethod.LabMethodName;
                                labMethodIDResult = cmd22.ExecuteScalar();
                                if (labMethodIDResult != null)
                                {
                                    labMethod.Id = Convert.ToInt32(labMethodIDResult);
                                }
                            }
                        }

                        //check unsaved lab methods
                        foreach (LabMethod lm in labMethodLookup.Values)
                        {
                            if (lm.Id == 0)
                            {
                                unsavedLabMethods.Add(lm);
                            }
                        }

                        //save lab methods
                        if (unsavedLabMethods.Count > 0)
                        {
                            using (DbCommand cmd23 = conn.CreateCommand())
                            {
                                cmd23.CommandText = sqlSaveLabMethod;
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (LabMethod labMethodToSave in unsavedLabMethods)
                                {
                                    cmd23.Parameters[0].Value = labMethodToSave.LabName;
                                    cmd23.Parameters[1].Value = labMethodToSave.LabOrganization;
                                    cmd23.Parameters[2].Value = labMethodToSave.LabMethodName;
                                    cmd23.Parameters[3].Value = labMethodToSave.LabMethodLink;
                                    cmd23.Parameters[4].Value = labMethodToSave.LabMethodDescription;
                                    labMethodIDResult = cmd23.ExecuteScalar();
                                    labMethodToSave.Id = Convert.ToInt32(labMethodIDResult);
                                }
                            }
                        }

                        //save samples
                        if (unsavedSamples.Count > 0)
                        {
                            using (DbCommand cmd24 = conn.CreateCommand())
                            {
                                cmd24.CommandText = sqlSaveSample;
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32));

                                foreach (Sample samp3 in unsavedSamples)
                                {
                                    cmd24.Parameters[0].Value = samp3.SampleType;
                                    cmd24.Parameters[1].Value = samp3.LabSampleCode;
                                    cmd24.Parameters[2].Value = samp3.LabMethod.Id;
                                    sampleIDResult = cmd24.ExecuteScalar();
                                    samp3.Id = Convert.ToInt32(sampleIDResult);
                                }
                            }
                        }
                    }



                    //****************************************************************
                    //*** TODO Step 11 Vertical Offsets
                    //****************************************************************
                    if (offsetLookup.Count > 0)
                    {
                        Dictionary<string, Unit> offsetUnitLookup = new Dictionary<string, Unit>();
                        List<Unit> unsavedOffsetUnits = new List<Unit>();

                        using (DbCommand cmd25 = conn.CreateCommand())
                        {
                            cmd25.CommandText = sqlOffsetType;
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (OffsetType offset in offsetLookup.Values)
                            {
                                cmd25.Parameters[0].Value = offset.Description;
                                offsetTypeIDResult = cmd25.ExecuteScalar();
                                if (offsetTypeIDResult != null)
                                {
                                    offset.Id = Convert.ToInt32(offsetTypeIDResult);
                                }
                            }
                        }

                        //check unsaved offsets
                        List<OffsetType> unsavedoffsets = new List<OffsetType>();
                        foreach (OffsetType offset2 in offsetLookup.Values)
                        {
                            if (offset2.Id == 0)
                            {
                                unsavedoffsets.Add(offset2);
                                string offsetUnitsKey = offset2.Unit.Abbreviation + "|" + offset2.Unit.Name;
                                if (!offsetUnitLookup.ContainsKey(offsetUnitsKey))
                                {
                                    offsetUnitLookup.Add(offsetUnitsKey, offset2.Unit);
                                }
                            }
                        }

                        //check for existing offset units
                        using (DbCommand cmd26 = conn.CreateCommand())
                        {
                            cmd26.CommandText = sqlUnits;
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Unit offsetUnit in offsetUnitLookup.Values)
                            {
                                cmd26.Parameters[0].Value = offsetUnit.Name;
                                cmd26.Parameters[1].Value = offsetUnit.UnitsType;
                                cmd26.Parameters[2].Value = offsetUnit.Abbreviation;
                                offsetUnitIDResult = cmd26.ExecuteScalar();
                                if (offsetUnitIDResult != null)
                                {
                                    offsetUnit.Id = Convert.ToInt32(offsetUnitIDResult);
                                }
                            }
                        }

                        //check unsaved offset unit
                        foreach (Unit offsetUnit1 in offsetUnitLookup.Values)
                        {
                            if (offsetUnit1.Id == 0)
                            {
                                unsavedOffsetUnits.Add(offsetUnit1);
                            }
                        }

                        //save offset units
                        if (unsavedOffsetUnits.Count > 0)
                        {
                            using (DbCommand cmd27 = conn.CreateCommand())
                            {
                                cmd27.CommandText = sqlSaveUnits;
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Unit unitToSave in unsavedOffsetUnits)
                                {
                                    cmd27.Parameters[0].Value = unitToSave.Name;
                                    cmd27.Parameters[1].Value = unitToSave.UnitsType;
                                    cmd27.Parameters[2].Value = unitToSave.Abbreviation;

                                    offsetUnitIDResult = cmd27.ExecuteScalar();
                                    unitToSave.Id = Convert.ToInt32(offsetUnitIDResult);
                                }
                            }
                        }

                        //save offset types
                        if (unsavedoffsets.Count > 0)
                        {
                            using (DbCommand cmd28 = conn.CreateCommand())
                            {
                                cmd28.CommandText = sqlSaveOffsetType;
                                cmd28.Parameters.Add(_db.CreateParameter(DbType.Int32));
                                cmd28.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (OffsetType offsetToSave in unsavedoffsets)
                                {
                                    cmd28.Parameters[0].Value = offsetToSave.Unit.Id;
                                    cmd28.Parameters[1].Value = offsetToSave.Description;
                                    offsetTypeIDResult = cmd28.ExecuteScalar();
                                    offsetToSave.Id = Convert.ToInt32(offsetTypeIDResult);
                                }
                            }
                        }
                    }

                    //****************************************************************
                    //*** TODO Step 12 Data File - QueryInfo - DataService ***********
                    //****************************************************************

                    //****************************************************************
                    //*** TODO Step 13 Data Values                         ***********
                    //****************************************************************
                    using (DbCommand cmd30 = conn.CreateCommand())
                    {
                        cmd30.CommandText = sqlSaveDataValue;
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.String));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));

                        foreach (DataValue val in series.DataValueList)
                        {
                            cmd30.Parameters[1].Value = val.Value;
                            cmd30.Parameters[2].Value = null;
                            if (val.ValueAccuracy != 0)
                            {
                                cmd30.Parameters[2].Value = val.ValueAccuracy;
                            }
                            cmd30.Parameters[3].Value = val.LocalDateTime;
                            cmd30.Parameters[4].Value = val.UTCOffset;
                            cmd30.Parameters[5].Value = val.DateTimeUTC;
                            if (val.OffsetType != null)
                            {
                                cmd30.Parameters[6].Value = val.OffsetValue;
                                cmd30.Parameters[7].Value = val.OffsetType.Id;
                            }
                            else
                            {
                                cmd30.Parameters[6].Value = null;
                                cmd30.Parameters[7].Value = null;
                            }
                            cmd30.Parameters[8].Value = val.CensorCode;
                            if (val.Qualifier != null)
                            {
                                cmd30.Parameters[9].Value = val.Qualifier.Id;
                            }

                            if (val.Sample != null)
                            {
                                cmd30.Parameters[10].Value = val.Sample.Id;
                            }

                            cmd30.Parameters[11].Value = null; //TODO Check Data File

                            cmd30.ExecuteNonQuery();
                            numSavedValues++;
                        }
                    }

                    //****************************************************************
                    //*** Step 14 Data Theme                               ***********
                    //****************************************************************
                    using (DbCommand cmd22 = conn.CreateCommand())
                    {
                        cmd22.CommandText = sqlTheme;
                        cmd22.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                        themeIDResult = cmd22.ExecuteScalar();
                        if (themeIDResult != null)
                        {
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    if (themeID == 0)
                    {
                        using (DbCommand cmd23 = conn.CreateCommand())
                        {
                            cmd23.CommandText = sqlSaveTheme1;
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Description));
                            themeIDResult = cmd23.ExecuteScalar();
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    using (DbCommand cmd24 = conn.CreateCommand())
                    {
                        cmd24.CommandText = sqlSaveTheme2;
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        cmd24.ExecuteNonQuery();
                    }

                    //Step 13 Commit Transaction
                    tran.Commit();
                }
                conn.Close();
            }
            return numSavedValues;
        }

        /// <summary>
        /// Saves a data series to the database. The series will be associated with the 
        /// specified theme. Depending on the OverwriteOptions, this will check if the series
        /// already exists in the database and overwrite data values in the database if required. 
        /// </summary>
        /// <param name="seriesToSave">The data series to be saved. This should contain
        /// information about site, variable, method, source and quality control level.</param>
        /// <param name="theme">The theme where this series should belong to</param>
        /// <param name="overwrite">The overwrite options. Set this to 'Copy' if 
        /// a new series should be created in the database. For options other than 'Copy',
        /// some of the existing data values in the database may be overwritten.</param>
        public int SaveSeries(Series seriesToSave, Theme theme, OverwriteOptions overwrite)
        {
            if (overwrite == OverwriteOptions.Append || overwrite == OverwriteOptions.Fill)
            {
                return SaveSeriesAppend(seriesToSave, theme);
            }
            else if (overwrite == OverwriteOptions.Copy)
            {
                return SaveSeriesAsCopy(seriesToSave, theme);
            }
            else if (overwrite == OverwriteOptions.Overwrite)
            {
                return SaveSeriesOverwrite(seriesToSave, theme);
            }
            else
            {
                //default option is 'append'...
                return SaveSeriesAppend(seriesToSave, theme);
            }
        }

        /// <summary>
        /// Saves a data series to the database. The series will be associated with the 
        /// specified theme. This method checks whether there are any existing series with 
        /// the same properties in the database. If there are existing series with the same
        /// properties, the new data values are 'appended' to the existing series (no duplicate
        /// series or data values are created)
        /// </summary>
        /// <param name="series">The time series</param>
        /// <param name="theme">The associated theme</param>
        /// <returns>Number of DataValue saved</returns>
        private int SaveSeriesAppend(Series series, Theme theme)
        {
            string sqlSite = "SELECT SiteID FROM Sites WHERE SiteCode = ?";
            string sqlVariable = "SELECT VariableID FROM Variables WHERE VariableCode = ? AND DataType = ?";
            string sqlSpatialReference = "SELECT SpatialReferenceID FROM SpatialReferences WHERE SRSID = ? AND SRSName = ?";
            string sqlUnits = "SELECT UnitsID FROM Units WHERE UnitsName = ? AND UnitsType = ? AND UnitsAbbreviation = ?";
            string sqlMethod = "SELECT MethodID FROM Methods WHERE MethodDescription = ?";
            string sqlSource = "SELECT SourceID FROM Sources WHERE Organization = ?";
            string sqlISOMetadata = "SELECT MetadataID FROM ISOMetadata WHERE Title = ? AND MetadataLink = ?";
            string sqlQuality = "SELECT QualityControlLevelID FROM QualityControlLevels WHERE Definition = ?";
            string sqlQualifier = "SELECT QualifierID FROM Qualifiers WHERE QualifierCode = ?";
            string sqlSample = "SELECT SampleID FROM Samples WHERE SampleType = ? AND LabSampleCode = ?";
            string sqlLabMethod = "SELECT LabMethodID FROM LabMethods WHERE LabName = ? AND LabMethodName = ?";
            string sqlOffsetType = "SELECT OffsetTypeID FROM OffsetTypes WHERE OffsetDescription = ?";
            string sqlTheme = "SELECT ThemeID FROM DataThemeDescriptions WHERE ThemeName = ?";
            string sqlThemeSeries = "SELECT ThemeID FROM DataThemes WHERE ThemeID = ? AND SeriesID = ?";
            string sqlRowID = "; SELECT LAST_INSERT_ROWID();";
            string sqlSeries = "SELECT SeriesID, BeginDateTime, BeginDateTimeUTC, EndDateTime, EndDateTimeUTC, ValueCount FROM DataSeries WHERE SiteID = ? AND VariableID = ? AND MethodID = ? AND QualityControlLevelID = ? AND SourceID = ?";

            string sqlSaveSpatialReference = "INSERT INTO SpatialReferences(SRSID, SRSName) VALUES(?, ?)" + sqlRowID;

            string sqlSaveSite = "INSERT INTO Sites(SiteCode, SiteName, Latitude, Longitude, LatLongDatumID, Elevation_m, VerticalDatum, " +
                                                   "LocalX, LocalY, LocalProjectionID, PosAccuracy_m, State, County, Comments) " +
                                                   "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveUnits = "INSERT INTO Units(UnitsName, UnitsType, UnitsAbbreviation) VALUES(?, ?, ?)" + sqlRowID;

            string sqlSaveVariable = "INSERT INTO Variables(VariableCode, VariableName, Speciation, VariableUnitsID, SampleMedium, ValueType, " +
                "IsRegular, ISCategorical, TimeSupport, TimeUnitsID, DataType, GeneralCategory, NoDataValue) " +
                "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveMethod = "INSERT INTO Methods(MethodDescription, MethodLink) VALUES(?, ?)" + sqlRowID;

            string sqlSaveQualityControl = "INSERT INTO QualityControlLevels(QualityControlLevelCode, Definition, Explanation) " +
                "VALUES(?,?,?)" + sqlRowID;

            string sqlSaveSource = "INSERT INTO Sources(Organization, SourceDescription, SourceLink, ContactName, Phone, " +
                                   "Email, Address, City, State, ZipCode, Citation, MetadataID) " +
                                   "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveISOMetadata = "INSERT INTO ISOMetadata(TopicCategory, Title, Abstract, ProfileVersion, MetadataLink) " +
                                    "VALUES(?,?,?,?,?)" + sqlRowID;

            string sqlSaveSeries = "INSERT INTO DataSeries(SiteID, VariableID, MethodID, SourceID, QualityControlLevelID, " +
                "IsCategorical, BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, CreationDateTime, " +
                "Subscribed, UpdateDateTime, LastCheckedDateTime) " +
                "VALUES(?, ?, ?, ?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveQualifier = "INSERT INTO Qualifiers(QualifierCode, QualifierDescription) VALUES (?,?)" + sqlRowID;

            string sqlSaveSample = "INSERT INTO Samples(SampleType, LabSampleCode, LabMethodID) VALUES (?,?, ?)" + sqlRowID;

            string sqlSaveLabMethod = "INSERT INTO LabMethods(LabName, LabOrganization, LabMethodName, LabMethodLink, LabMethodDescription) " +
                "VALUES(?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveOffsetType = "INSERT INTO OffsetTypes(OffsetUnitsID, OffsetDescription) VALUES (?, ?)" + sqlRowID;

            string sqlSaveDataValue = "INSERT INTO DataValues(SeriesID, DataValue, ValueAccuracy, LocalDateTime, " +
                "UTCOffset, DateTimeUTC, OffsetValue, OffsetTypeID, CensorCode, QualifierID, SampleID, FileID) " +
                "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)";

            string sqlSaveTheme1 = "INSERT INTO DataThemeDescriptions(ThemeName, ThemeDescription) VALUES (?,?)" + sqlRowID;
            string sqlSaveTheme2 = "INSERT INTO DataThemes(ThemeID,SeriesID) VALUEs (?,?)";

            string sqlUpdateSeries = "UPDATE DataSeries SET BeginDateTime = ?, BeginDateTimeUTC = ?, EndDateTime = ?, EndDateTimeUTC = ?, " +
                "ValueCount = ?, UpdateDateTime = ? WHERE SeriesID = ?";

            int siteID = 0;
            int variableID = 0;
            int spatialReferenceID = 0;
            int localProjectionID = 0;
            int variableUnitsID = 0;
            int timeUnitsID = 0;
            int methodID = 0;
            int qualityControlLevelID = 0;
            int sourceID = 0;
            int isoMetadataID = 0;
            int seriesID = 0;
            int themeID = 0;
            //int offsetTypeID = 0;

            object siteIDResult = null;
            object spatialReferenceIDResult = null;
            object localProjectionIDResult = null;
            object variableIDResult = null;
            object variableUnitsIDResult = null;
            object timeUnitsIDResult = null;
            object methodIDResult = null;
            object qualityControlLevelIDResult = null;
            object sourceIDResult = null;
            object isoMetadataIDResult = null;
            object seriesIDResult = null;
            object qualifierIDResult = null;
            object themeIDResult = null;
            object sampleIDResult = null;
            object labMethodIDResult = null;
            object offsetTypeIDResult = null;
            object offsetUnitIDResult = null;

            Dictionary<string, Qualifier> qualifierLookup = new Dictionary<string, Qualifier>();
            Dictionary<string, Sample> sampleLookup = new Dictionary<string, Sample>();
            Dictionary<string, OffsetType> offsetLookup = new Dictionary<string, OffsetType>();

            int numSavedValues = 0;

            bool seriesAlreadyExists = false;
            DateTime beginTimeDb = DateTime.MinValue;
            DateTime beginTimeUtcDb = beginTimeDb;
            DateTime endTimeDb = DateTime.MinValue;
            DateTime endTimeUtcDb = endTimeDb;
            int valueCountDb = 0;

            //Step 1 Begin Transaction
            using (DbConnection conn = _db.CreateConnection())
            {
                conn.Open();

                using (DbTransaction tran = conn.BeginTransaction())
                {
                    //****************************************************************
                    //*** Step 2 Site
                    //****************************************************************
                    using (DbCommand cmd01 = conn.CreateCommand())
                    {
                        cmd01.CommandText = sqlSite;
                        cmd01.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.Code));
                        siteIDResult = cmd01.ExecuteScalar();
                        if (siteIDResult != null)
                        {
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    if (siteID == 0) //New Site needs to be created
                    {
                        using (DbCommand cmd02 = conn.CreateCommand())
                        {
                            cmd02.CommandText = sqlSpatialReference;
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));

                            spatialReferenceIDResult = cmd02.ExecuteScalar();
                            if (spatialReferenceIDResult != null)
                            {
                                spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                            }

                            if (series.Site.LocalProjection != null)
                            {
                                cmd02.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                cmd02.Parameters[1].Value = series.Site.LocalProjection.SRSName;

                                localProjectionIDResult = cmd02.ExecuteScalar();
                                if (localProjectionIDResult != null)
                                {
                                    localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                }
                            }
                        }

                        if (spatialReferenceID == 0)
                        {
                            //save spatial reference and the local projection
                            using (DbCommand cmd03 = conn.CreateCommand())
                            {
                                //Save the spatial reference (Lat / Long Datum)
                                cmd03.CommandText = sqlSaveSpatialReference;
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));

                                spatialReferenceIDResult = cmd03.ExecuteScalar();

                                if (spatialReferenceIDResult != null)
                                {
                                    spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                                }

                                //Save the local projection
                                if (series.Site.LocalProjection != null)
                                {
                                    if (localProjectionID == 0)
                                    {
                                        cmd03.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                        cmd03.Parameters[1].Value = series.Site.LocalProjection.SRSName;
                                        localProjectionIDResult = cmd03.ExecuteScalar();
                                        localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                    }
                                }
                            }
                        }

                        //Insert the site to the database
                        using (DbCommand cmd04 = conn.CreateCommand())
                        {
                            Site site = series.Site;

                            cmd04.CommandText = sqlSaveSite;
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Code));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Name));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Latitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Longitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, spatialReferenceID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Elevation_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.VerticalDatum));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalX));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalY));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, localProjectionID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.PosAccuracy_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.State));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.County));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Comments));

                            siteIDResult = cmd04.ExecuteScalar();
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 3 Variable
                    //****************************************************************
                    Variable variable = series.Variable;

                    using (DbCommand cmd05 = conn.CreateCommand())
                    {
                        cmd05.CommandText = sqlVariable;
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                        cmd05.Parameters[0].Value = variable.Code;
                        cmd05.Parameters[1].Value = variable.DataType;
                        variableIDResult = cmd05.ExecuteScalar();
                        if (variableIDResult != null)
                        {
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    if (variableID == 0) //New variable needs to be created
                    {
                        using (DbCommand cmd06 = conn.CreateCommand())
                        {
                            cmd06.CommandText = sqlUnits;
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));

                            variableUnitsIDResult = cmd06.ExecuteScalar();
                            if (variableUnitsIDResult != null)
                            {
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }

                            cmd06.Parameters[0].Value = variable.TimeUnit.Name;
                            cmd06.Parameters[1].Value = variable.TimeUnit.UnitsType;
                            cmd06.Parameters[2].Value = variable.TimeUnit.Abbreviation;
                            timeUnitsIDResult = cmd06.ExecuteScalar();
                            if (timeUnitsIDResult != null)
                            {
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        if (variableUnitsID == 0)
                        {
                            //save the variable units
                            using (DbCommand cmd07 = conn.CreateCommand())
                            {
                                //Save the variable units
                                cmd07.CommandText = sqlSaveUnits;
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));
                                variableUnitsIDResult = cmd07.ExecuteScalar();
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }
                        }

                        if (timeUnitsID == 0)
                        {
                            //save the time units
                            using (DbCommand cmd08 = conn.CreateCommand())
                            {
                                //Save the time units
                                cmd08.CommandText = sqlSaveUnits;
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Name));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.UnitsType));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Abbreviation));
                                timeUnitsIDResult = cmd08.ExecuteScalar();
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        //Insert the variable to the database
                        using (DbCommand cmd09 = conn.CreateCommand())
                        {
                            cmd09.CommandText = sqlSaveVariable;
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Name));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Speciation));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, variableUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.SampleMedium));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.ValueType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsRegular));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsCategorical));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.TimeSupport));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, timeUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.GeneralCategory));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.NoDataValue));

                            variableIDResult = cmd09.ExecuteScalar();
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 4 Method
                    //****************************************************************
                    Method method = series.Method;

                    using (DbCommand cmd10 = conn.CreateCommand())
                    {
                        cmd10.CommandText = sqlMethod;
                        cmd10.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                        methodIDResult = cmd10.ExecuteScalar();
                        if (methodIDResult != null)
                        {
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    if (methodID == 0)
                    {
                        using (DbCommand cmd11 = conn.CreateCommand())
                        {
                            cmd11.CommandText = sqlSaveMethod;
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Link));
                            methodIDResult = cmd11.ExecuteScalar();
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 5 Quality Control Level
                    //****************************************************************
                    QualityControlLevel qc = series.QualityControlLevel;

                    using (DbCommand cmd12 = conn.CreateCommand())
                    {
                        cmd12.CommandText = sqlQuality;
                        cmd12.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                        qualityControlLevelIDResult = cmd12.ExecuteScalar();
                        if (qualityControlLevelIDResult != null)
                        {
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    if (qualityControlLevelID == 0)
                    {
                        using (DbCommand cmd13 = conn.CreateCommand())
                        {
                            cmd13.CommandText = sqlSaveQualityControl;
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Code));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Explanation));
                            qualityControlLevelIDResult = cmd13.ExecuteScalar();
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 6 Source
                    //****************************************************************
                    Source source = series.Source;

                    using (DbCommand cmd14 = conn.CreateCommand())
                    {
                        cmd14.CommandText = sqlSource;
                        cmd14.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                        sourceIDResult = cmd14.ExecuteScalar();
                        if (sourceIDResult != null)
                        {
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    if (sourceID == 0)
                    {
                        ISOMetadata isoMetadata = source.ISOMetadata;

                        using (DbCommand cmd15 = conn.CreateCommand())
                        {
                            cmd15.CommandText = sqlISOMetadata;
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                            isoMetadataIDResult = cmd15.ExecuteScalar();
                            if (isoMetadataIDResult != null)
                            {
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        if (isoMetadataID == 0)
                        {
                            using (DbCommand cmd16 = conn.CreateCommand())
                            {
                                cmd16.CommandText = sqlSaveISOMetadata;
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.TopicCategory));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Abstract));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.ProfileVersion));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                                isoMetadataIDResult = cmd16.ExecuteScalar();
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        using (DbCommand cmd17 = conn.CreateCommand())
                        {
                            cmd17.CommandText = sqlSaveSource;
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Description));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Link));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.ContactName));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Phone));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Email));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Address));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.City));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.State));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.Int32, source.ZipCode));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Citation));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadataID));
                            sourceIDResult = cmd17.ExecuteScalar();
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 7 Series
                    //****************************************************************
                    seriesIDResult = null;
                    using (DbCommand cmdSeries = conn.CreateCommand())
                    {
                        //To retrieve the BeginTime, EndTime and SeriesID of the existing series
                        cmdSeries.CommandText = sqlSeries;
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));

                        DbDataReader reader = cmdSeries.ExecuteReader(CommandBehavior.SingleRow);
                        if (reader.HasRows)
                        {
                            seriesIDResult = reader[0];
                            try
                            {
                                beginTimeDb = Convert.ToDateTime(reader[1]);
                                beginTimeUtcDb = Convert.ToDateTime(reader[2]);
                                endTimeDb = Convert.ToDateTime(reader[3]);
                                endTimeUtcDb = Convert.ToDateTime(reader[4]);
                                valueCountDb = Convert.ToInt32(reader[5]);
                            }
                            catch { }
                            finally
                            {
                                reader.Close();
                                reader.Dispose();
                            }
                        }
                    }


                    if (seriesIDResult != null && beginTimeDb > DateTime.MinValue && endTimeDb > DateTime.MinValue)
                    {
                        //Case 1: Series Already Exists.
                        seriesAlreadyExists = true;
                        seriesID = Convert.ToInt32(seriesIDResult);

                        //If the series already exists, don't save any data values within the existing series time range
                        //to do this, we remove data values that should not be saved from the DataValueList                    
                        for (int i = series.DataValueList.Count - 1; i >= 0; i--)
                        {
                            DataValue val = series.DataValueList[i];
                            if (val.LocalDateTime >= beginTimeDb && val.LocalDateTime <= endTimeDb)
                            {
                                series.DataValueList.Remove(val);
                            }
                        }
                    }
                    else
                    {
                        //Case 2: Series does not exist.
                        seriesAlreadyExists = false;
                        using (DbCommand cmd18 = conn.CreateCommand())
                        {
                            cmd18.CommandText = sqlSaveSeries;
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.IsCategorical));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTimeUTC));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTimeUTC));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, series.ValueCount));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.CreationDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.Subscribed));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.UpdateDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.LastCheckedDateTime));

                            seriesIDResult = cmd18.ExecuteScalar();
                            seriesID = Convert.ToInt32(seriesIDResult);
                        }
                    }

                    //**********************************************************************
                    //*** Data Value - related: Only if new data values are saved **********
                    //**********************************************************************
                    if (series.DataValueList.Count > 0)
                    {

                        //****************************************************************
                        //*** Step 8 Qualifier and Sample Lookup
                        //****************************************************************
                        foreach (DataValue val in series.DataValueList)
                        {
                            if (val.Qualifier != null)
                            {
                                if (!qualifierLookup.ContainsKey(val.Qualifier.Code))
                                {
                                    qualifierLookup.Add(val.Qualifier.Code, val.Qualifier);
                                }
                            }

                            if (val.Sample != null)
                            {
                                if (!sampleLookup.ContainsKey(val.Sample.LabSampleCode))
                                {
                                    sampleLookup.Add(val.Sample.LabSampleCode, val.Sample);
                                }
                            }
                            if (val.OffsetType != null)
                            {
                                if (!offsetLookup.ContainsKey(val.OffsetType.Description))
                                {
                                    offsetLookup.Add(val.OffsetType.Description, val.OffsetType);
                                }
                            }
                        }

                        //****************************************************************
                        //*** Step 9 Qualifiers
                        //****************************************************************
                        if (qualifierLookup.Count > 0)
                        {
                            using (DbCommand cmd19 = conn.CreateCommand())
                            {
                                cmd19.CommandText = sqlQualifier;
                                cmd19.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Qualifier qualifier in qualifierLookup.Values)
                                {
                                    cmd19.Parameters[0].Value = qualifier.Code;
                                    qualifierIDResult = cmd19.ExecuteScalar();
                                    if (qualifierIDResult != null)
                                    {
                                        qualifier.Id = Convert.ToInt32(qualifierIDResult);
                                    }
                                }
                            }

                            List<Qualifier> unsavedQualifiers = new List<Qualifier>();
                            foreach (Qualifier qual in qualifierLookup.Values)
                            {
                                if (qual.Id == 0)
                                {
                                    unsavedQualifiers.Add(qual);
                                }
                            }

                            if (unsavedQualifiers.Count > 0)
                            {
                                using (DbCommand cmd20 = conn.CreateCommand())
                                {
                                    cmd20.CommandText = sqlSaveQualifier;
                                    cmd20.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd20.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (Qualifier qual2 in unsavedQualifiers)
                                    {
                                        cmd20.Parameters[0].Value = qual2.Code;
                                        cmd20.Parameters[1].Value = qual2.Description;
                                        qualifierIDResult = cmd20.ExecuteScalar();
                                        qual2.Id = Convert.ToInt32(qualifierIDResult);
                                    }
                                }
                            }
                        }

                        //****************************************************************
                        //*** TODO Step 10 Samples and Lab Methods
                        //****************************************************************
                        if (sampleLookup.Count > 0)
                        {
                            Dictionary<string, LabMethod> labMethodLookup = new Dictionary<string, LabMethod>();

                            using (DbCommand cmd21 = conn.CreateCommand())
                            {
                                cmd21.CommandText = sqlSample;
                                cmd21.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd21.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Sample sample in sampleLookup.Values)
                                {
                                    cmd21.Parameters[0].Value = sample.SampleType;
                                    cmd21.Parameters[1].Value = sample.LabSampleCode;
                                    sampleIDResult = cmd21.ExecuteScalar();
                                    if (sampleIDResult != null)
                                    {
                                        sample.Id = Convert.ToInt32(sampleIDResult);
                                    }
                                }
                            }


                            List<Sample> unsavedSamples = new List<Sample>();
                            List<LabMethod> unsavedLabMethods = new List<LabMethod>();

                            foreach (Sample samp in sampleLookup.Values)
                            {
                                if (samp.Id == 0)
                                {
                                    unsavedSamples.Add(samp);
                                    string labMethodKey = samp.LabMethod.LabName + "|" + samp.LabMethod.LabMethodName;
                                    if (!labMethodLookup.ContainsKey(labMethodKey))
                                    {
                                        labMethodLookup.Add(labMethodKey, samp.LabMethod);
                                    }
                                }
                            }

                            using (DbCommand cmd22 = conn.CreateCommand())
                            {
                                cmd22.CommandText = sqlLabMethod;
                                cmd22.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd22.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (LabMethod labMethod in labMethodLookup.Values)
                                {
                                    cmd22.Parameters[0].Value = labMethod.LabName;
                                    cmd22.Parameters[1].Value = labMethod.LabMethodName;
                                    labMethodIDResult = cmd22.ExecuteScalar();
                                    if (labMethodIDResult != null)
                                    {
                                        labMethod.Id = Convert.ToInt32(labMethodIDResult);
                                    }
                                }
                            }

                            //check unsaved lab methods
                            foreach (LabMethod lm in labMethodLookup.Values)
                            {
                                if (lm.Id == 0)
                                {
                                    unsavedLabMethods.Add(lm);
                                }
                            }

                            //save lab methods
                            if (unsavedLabMethods.Count > 0)
                            {
                                using (DbCommand cmd23 = conn.CreateCommand())
                                {
                                    cmd23.CommandText = sqlSaveLabMethod;
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (LabMethod labMethodToSave in unsavedLabMethods)
                                    {
                                        cmd23.Parameters[0].Value = labMethodToSave.LabName;
                                        cmd23.Parameters[1].Value = labMethodToSave.LabOrganization;
                                        cmd23.Parameters[2].Value = labMethodToSave.LabMethodName;
                                        cmd23.Parameters[3].Value = labMethodToSave.LabMethodLink;
                                        cmd23.Parameters[4].Value = labMethodToSave.LabMethodDescription;
                                        labMethodIDResult = cmd23.ExecuteScalar();
                                        labMethodToSave.Id = Convert.ToInt32(labMethodIDResult);
                                    }
                                }
                            }

                            //save samples
                            if (unsavedSamples.Count > 0)
                            {
                                using (DbCommand cmd24 = conn.CreateCommand())
                                {
                                    cmd24.CommandText = sqlSaveSample;
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32));

                                    foreach (Sample samp3 in unsavedSamples)
                                    {
                                        cmd24.Parameters[0].Value = samp3.SampleType;
                                        cmd24.Parameters[1].Value = samp3.LabSampleCode;
                                        cmd24.Parameters[2].Value = samp3.LabMethod.Id;
                                        sampleIDResult = cmd24.ExecuteScalar();
                                        samp3.Id = Convert.ToInt32(sampleIDResult);
                                    }
                                }
                            }
                        }



                        //****************************************************************
                        //*** TODO Step 11 Vertical Offsets (NEEDS TESTING DATA - DCEW)
                        //****************************************************************
                        if (offsetLookup.Count > 0)
                        {
                            Dictionary<string, Unit> offsetUnitLookup = new Dictionary<string, Unit>();
                            List<Unit> unsavedOffsetUnits = new List<Unit>();

                            using (DbCommand cmd25 = conn.CreateCommand())
                            {
                                cmd25.CommandText = sqlOffsetType;
                                cmd25.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (OffsetType offset in offsetLookup.Values)
                                {
                                    cmd25.Parameters[0].Value = offset.Description;
                                    offsetTypeIDResult = cmd25.ExecuteScalar();
                                    if (offsetTypeIDResult != null)
                                    {
                                        offset.Id = Convert.ToInt32(offsetTypeIDResult);
                                    }
                                }
                            }

                            //check unsaved offsets
                            List<OffsetType> unsavedoffsets = new List<OffsetType>();
                            foreach (OffsetType offset2 in offsetLookup.Values)
                            {
                                if (offset2.Id == 0)
                                {
                                    unsavedoffsets.Add(offset2);
                                    string offsetUnitsKey = offset2.Unit.Abbreviation + "|" + offset2.Unit.Name;
                                    if (!offsetUnitLookup.ContainsKey(offsetUnitsKey))
                                    {
                                        offsetUnitLookup.Add(offsetUnitsKey, offset2.Unit);
                                    }
                                }
                            }

                            //check for existing offset units
                            using (DbCommand cmd26 = conn.CreateCommand())
                            {
                                cmd26.CommandText = sqlUnits;
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Unit offsetUnit in offsetUnitLookup.Values)
                                {
                                    cmd26.Parameters[0].Value = offsetUnit.Name;
                                    cmd26.Parameters[1].Value = offsetUnit.UnitsType;
                                    cmd26.Parameters[2].Value = offsetUnit.Abbreviation;
                                    offsetUnitIDResult = cmd26.ExecuteScalar();
                                    if (offsetUnitIDResult != null)
                                    {
                                        offsetUnit.Id = Convert.ToInt32(offsetUnitIDResult);
                                    }
                                }
                            }

                            //check unsaved offset unit
                            foreach (Unit offsetUnit1 in offsetUnitLookup.Values)
                            {
                                if (offsetUnit1.Id == 0)
                                {
                                    unsavedOffsetUnits.Add(offsetUnit1);
                                }
                            }

                            //save offset units
                            if (unsavedOffsetUnits.Count > 0)
                            {
                                using (DbCommand cmd27 = conn.CreateCommand())
                                {
                                    cmd27.CommandText = sqlSaveUnits;
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (Unit unitToSave in unsavedOffsetUnits)
                                    {
                                        cmd27.Parameters[0].Value = unitToSave.Name;
                                        cmd27.Parameters[1].Value = unitToSave.UnitsType;
                                        cmd27.Parameters[2].Value = unitToSave.Abbreviation;

                                        offsetUnitIDResult = cmd27.ExecuteScalar();
                                        unitToSave.Id = Convert.ToInt32(offsetUnitIDResult);
                                    }
                                }
                            }

                            //save offset types
                            if (unsavedoffsets.Count > 0)
                            {
                                using (DbCommand cmd28 = conn.CreateCommand())
                                {
                                    cmd28.CommandText = sqlSaveOffsetType;
                                    cmd28.Parameters.Add(_db.CreateParameter(DbType.Int32));
                                    cmd28.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (OffsetType offsetToSave in unsavedoffsets)
                                    {
                                        cmd28.Parameters[0].Value = offsetToSave.Unit.Id;
                                        cmd28.Parameters[1].Value = offsetToSave.Description;
                                        offsetTypeIDResult = cmd28.ExecuteScalar();
                                        offsetToSave.Id = Convert.ToInt32(offsetTypeIDResult);
                                    }
                                }
                            }
                        }

                        //****************************************************************
                        //*** TODO Step 12 Data File - QueryInfo - DataService ***********
                        //****************************************************************

                        //****************************************************************
                        //*** TODO Step 13 Data Values related information     ***********
                        //****************************************************************

                        using (DbCommand cmd30 = conn.CreateCommand())
                        {
                            cmd30.CommandText = sqlSaveDataValue;
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));

                            foreach (DataValue val in series.DataValueList)
                            {
                                cmd30.Parameters[1].Value = val.Value;
                                cmd30.Parameters[2].Value = null;
                                if (val.ValueAccuracy != 0)
                                {
                                    cmd30.Parameters[2].Value = val.ValueAccuracy;
                                }
                                cmd30.Parameters[3].Value = val.LocalDateTime;
                                cmd30.Parameters[4].Value = val.UTCOffset;
                                cmd30.Parameters[5].Value = val.DateTimeUTC;
                                if (val.OffsetType != null)
                                {
                                    cmd30.Parameters[6].Value = val.OffsetValue;
                                    cmd30.Parameters[7].Value = val.OffsetType.Id;
                                }
                                else
                                {
                                    cmd30.Parameters[6].Value = null;
                                    cmd30.Parameters[7].Value = null;
                                }
                                cmd30.Parameters[8].Value = val.CensorCode;
                                if (val.Qualifier != null)
                                {
                                    cmd30.Parameters[9].Value = val.Qualifier.Id;
                                }

                                if (val.Sample != null)
                                {
                                    cmd30.Parameters[10].Value = val.Sample.Id;
                                }

                                cmd30.Parameters[11].Value = null; //TODO Check Data File

                                cmd30.ExecuteNonQuery();
                                numSavedValues++;
                            }
                        }

                        //****************************************************************
                        //*** Step 14 Data Series Update                       ***********
                        //****************************************************************
                        if (seriesAlreadyExists == true && seriesID > 0)
                        {
                            //begin DateTime
                            DateTime beginDateTime = beginTimeDb;
                            DateTime beginDateTimeUTC = beginTimeDb;
                            if (series.BeginDateTime < beginTimeDb)
                            {
                                beginDateTime = series.BeginDateTime;
                                beginDateTimeUTC = series.BeginDateTimeUTC;
                            }

                            //end DateTime
                            DateTime endDateTime = endTimeDb;
                            DateTime endDateTimeUTC = endTimeDb;
                            if (series.EndDateTime > endTimeDb)
                            {
                                endDateTime = series.EndDateTime;
                                endDateTimeUTC = series.EndDateTimeUTC;
                            }

                            //valueCount and UpdateDateTime
                            int valueCount = valueCountDb + series.ValueCount;
                            DateTime updateDateTime = DateTime.Now;

                            using (DbCommand cmdUpdateSeries = conn.CreateCommand())
                            {
                                cmdUpdateSeries.CommandText = sqlUpdateSeries;
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, beginDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, beginDateTimeUTC));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, endDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, endDateTimeUTC));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, valueCount));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, updateDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));

                                cmdUpdateSeries.ExecuteNonQuery();
                            }
                        }
                    } //End of (If DataValueList.Count > 0)

                    //****************************************************************
                    //*** Step 15 Data Theme                               ***********
                    //****************************************************************

                    
                    using (DbCommand cmd22 = conn.CreateCommand())
                    {
                        cmd22.CommandText = sqlTheme;
                        cmd22.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                        themeIDResult = cmd22.ExecuteScalar();
                        if (themeIDResult != null)
                        {
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    if (themeID == 0)
                    {
                        using (DbCommand cmd23 = conn.CreateCommand())
                        {
                            cmd23.CommandText = sqlSaveTheme1;
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Description));
                            themeIDResult = cmd23.ExecuteScalar();
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    //To save the Theme-Series combination (DataThemes DataTable)
                    object seriesThemeCombinationResult = null;
                    using (DbCommand cmd24 = conn.CreateCommand())
                    {
                        cmd24.CommandText = sqlThemeSeries;
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        seriesThemeCombinationResult = cmd24.ExecuteScalar();
                    }

                    if (seriesThemeCombinationResult == null)
                    {
                        using (DbCommand cmd25 = conn.CreateCommand())
                        {
                            cmd25.CommandText = sqlSaveTheme2;
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                            cmd25.ExecuteNonQuery();
                        }
                    }
                    
                    
                    //Step 13 Commit Transaction
                    tran.Commit();
                }
                conn.Close();
            }
            return numSavedValues;
        }

        /// <summary>
        /// Saves a data series to the database. The series will be associated with the 
        /// specified theme. This method checks whether there is any existing series with 
        /// the same site, variable, method, source and QCLevel in the database. If there 
        /// is an existing series with the same properties, the existing series is deleted
        /// and it is replaced by the new series.
        /// </summary>
        /// <param name="series">The time series</param>
        /// <param name="theme">The associated theme</param>
        /// <returns>Number of DataValue saved</returns>
        private int SaveSeriesOverwrite(Series series, Theme theme)
        {
            string sqlSite = "SELECT SiteID FROM Sites WHERE SiteCode = ?";
            string sqlVariable = "SELECT VariableID FROM Variables WHERE VariableCode = ? AND DataType = ?";
            string sqlSpatialReference = "SELECT SpatialReferenceID FROM SpatialReferences WHERE SRSID = ? AND SRSName = ?";
            string sqlUnits = "SELECT UnitsID FROM Units WHERE UnitsName = ? AND UnitsType = ? AND UnitsAbbreviation = ?";
            string sqlMethod = "SELECT MethodID FROM Methods WHERE MethodDescription = ?";
            string sqlSource = "SELECT SourceID FROM Sources WHERE Organization = ?";
            string sqlISOMetadata = "SELECT MetadataID FROM ISOMetadata WHERE Title = ? AND MetadataLink = ?";
            string sqlQuality = "SELECT QualityControlLevelID FROM QualityControlLevels WHERE Definition = ?";
            string sqlQualifier = "SELECT QualifierID FROM Qualifiers WHERE QualifierCode = ?";
            string sqlSample = "SELECT SampleID FROM Samples WHERE SampleType = ? AND LabSampleCode = ?";
            string sqlLabMethod = "SELECT LabMethodID FROM LabMethods WHERE LabName = ? AND LabMethodName = ?";
            string sqlOffsetType = "SELECT OffsetTypeID FROM OffsetTypes WHERE OffsetDescription = ?";
            string sqlTheme = "SELECT ThemeID FROM DataThemeDescriptions WHERE ThemeName = ?";
            string sqlThemeSeries = "SELECT ThemeID FROM DataThemes WHERE ThemeID = ? AND SeriesID = ?";
            string sqlRowID = "; SELECT LAST_INSERT_ROWID();";
            string sqlSeries = "SELECT SeriesID, BeginDateTime, BeginDateTimeUTC, EndDateTime, EndDateTimeUTC, ValueCount FROM DataSeries WHERE SiteID = ? AND VariableID = ? AND MethodID = ? AND QualityControlLevelID = ? AND SourceID = ?";

            string sqlSaveSpatialReference = "INSERT INTO SpatialReferences(SRSID, SRSName) VALUES(?, ?)" + sqlRowID;

            string sqlSaveSite = "INSERT INTO Sites(SiteCode, SiteName, Latitude, Longitude, LatLongDatumID, Elevation_m, VerticalDatum, " +
                                                   "LocalX, LocalY, LocalProjectionID, PosAccuracy_m, State, County, Comments) " +
                                                   "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveUnits = "INSERT INTO Units(UnitsName, UnitsType, UnitsAbbreviation) VALUES(?, ?, ?)" + sqlRowID;

            string sqlSaveVariable = "INSERT INTO Variables(VariableCode, VariableName, Speciation, VariableUnitsID, SampleMedium, ValueType, " +
                "IsRegular, ISCategorical, TimeSupport, TimeUnitsID, DataType, GeneralCategory, NoDataValue) " +
                "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveMethod = "INSERT INTO Methods(MethodDescription, MethodLink) VALUES(?, ?)" + sqlRowID;

            string sqlSaveQualityControl = "INSERT INTO QualityControlLevels(QualityControlLevelCode, Definition, Explanation) " +
                "VALUES(?,?,?)" + sqlRowID;

            string sqlSaveSource = "INSERT INTO Sources(Organization, SourceDescription, SourceLink, ContactName, Phone, " +
                                   "Email, Address, City, State, ZipCode, Citation, MetadataID) " +
                                   "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveISOMetadata = "INSERT INTO ISOMetadata(TopicCategory, Title, Abstract, ProfileVersion, MetadataLink) " +
                                    "VALUES(?,?,?,?,?)" + sqlRowID;

            string sqlSaveSeries = "INSERT INTO DataSeries(SiteID, VariableID, MethodID, SourceID, QualityControlLevelID, " +
                "IsCategorical, BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, CreationDateTime, " +
                "Subscribed, UpdateDateTime, LastCheckedDateTime) " +
                "VALUES(?, ?, ?, ?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveQualifier = "INSERT INTO Qualifiers(QualifierCode, QualifierDescription) VALUES (?,?)" + sqlRowID;

            string sqlSaveSample = "INSERT INTO Samples(SampleType, LabSampleCode, LabMethodID) VALUES (?,?, ?)" + sqlRowID;

            string sqlSaveLabMethod = "INSERT INTO LabMethods(LabName, LabOrganization, LabMethodName, LabMethodLink, LabMethodDescription) " +
                "VALUES(?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveOffsetType = "INSERT INTO OffsetTypes(OffsetUnitsID, OffsetDescription) VALUES (?, ?)" + sqlRowID;

            string sqlSaveDataValue = "INSERT INTO DataValues(SeriesID, DataValue, ValueAccuracy, LocalDateTime, " +
                "UTCOffset, DateTimeUTC, OffsetValue, OffsetTypeID, CensorCode, QualifierID, SampleID, FileID) " +
                "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)";

            string sqlSaveTheme1 = "INSERT INTO DataThemeDescriptions(ThemeName, ThemeDescription) VALUES (?,?)" + sqlRowID;
            string sqlSaveTheme2 = "INSERT INTO DataThemes(ThemeID,SeriesID) VALUEs (?,?)";

            string sqlUpdateSeries = "UPDATE DataSeries SET BeginDateTime = ?, BeginDateTimeUTC = ?, EndDateTime = ?, EndDateTimeUTC = ?, " +
                "ValueCount = ?, UpdateDateTime = ? WHERE SeriesID = ?";

            string sqlDeleteValues = "DELETE FROM DataValues WHERE SeriesID = ? AND LocalDateTime >= ? AND LocalDateTime <= ?";

            int siteID = 0;
            int variableID = 0;
            int spatialReferenceID = 0;
            int localProjectionID = 0;
            int variableUnitsID = 0;
            int timeUnitsID = 0;
            int methodID = 0;
            int qualityControlLevelID = 0;
            int sourceID = 0;
            int isoMetadataID = 0;
            int seriesID = 0;
            int themeID = 0;
            //int offsetTypeID = 0;

            object siteIDResult = null;
            object spatialReferenceIDResult = null;
            object localProjectionIDResult = null;
            object variableIDResult = null;
            object variableUnitsIDResult = null;
            object timeUnitsIDResult = null;
            object methodIDResult = null;
            object qualityControlLevelIDResult = null;
            object sourceIDResult = null;
            object isoMetadataIDResult = null;
            object seriesIDResult = null;
            object qualifierIDResult = null;
            object themeIDResult = null;
            object sampleIDResult = null;
            object labMethodIDResult = null;
            object offsetTypeIDResult = null;
            object offsetUnitIDResult = null;

            Dictionary<string, Qualifier> qualifierLookup = new Dictionary<string, Qualifier>();
            Dictionary<string, Sample> sampleLookup = new Dictionary<string, Sample>();
            Dictionary<string, OffsetType> offsetLookup = new Dictionary<string, OffsetType>();

            int numSavedValues = 0;

            bool seriesAlreadyExists = false;
            DateTime beginTimeDb = DateTime.MinValue;
            DateTime beginTimeUtcDb = beginTimeDb;
            DateTime endTimeDb = DateTime.MinValue;
            DateTime endTimeUtcDb = endTimeDb;
            int valueCountDb = 0;

            //Step 1 Begin Transaction
            using (DbConnection conn = _db.CreateConnection())
            {
                conn.Open();

                using (DbTransaction tran = conn.BeginTransaction())
                {
                    //****************************************************************
                    //*** Step 2 Site
                    //****************************************************************
                    using (DbCommand cmd01 = conn.CreateCommand())
                    {
                        cmd01.CommandText = sqlSite;
                        cmd01.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.Code));
                        siteIDResult = cmd01.ExecuteScalar();
                        if (siteIDResult != null)
                        {
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    if (siteID == 0) //New Site needs to be created
                    {
                        using (DbCommand cmd02 = conn.CreateCommand())
                        {
                            cmd02.CommandText = sqlSpatialReference;
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));

                            spatialReferenceIDResult = cmd02.ExecuteScalar();
                            if (spatialReferenceIDResult != null)
                            {
                                spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                            }

                            if (series.Site.LocalProjection != null)
                            {
                                cmd02.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                cmd02.Parameters[1].Value = series.Site.LocalProjection.SRSName;

                                localProjectionIDResult = cmd02.ExecuteScalar();
                                if (localProjectionIDResult != null)
                                {
                                    localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                }
                            }
                        }

                        if (spatialReferenceID == 0)
                        {
                            //save spatial reference and the local projection
                            using (DbCommand cmd03 = conn.CreateCommand())
                            {
                                //Save the spatial reference (Lat / Long Datum)
                                cmd03.CommandText = sqlSaveSpatialReference;
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));

                                spatialReferenceIDResult = cmd03.ExecuteScalar();

                                if (spatialReferenceIDResult != null)
                                {
                                    spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                                }

                                //Save the local projection
                                if (series.Site.LocalProjection != null)
                                {
                                    if (localProjectionID == 0)
                                    {
                                        cmd03.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                        cmd03.Parameters[1].Value = series.Site.LocalProjection.SRSName;
                                        localProjectionIDResult = cmd03.ExecuteScalar();
                                        localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                    }
                                }
                            }
                        }

                        //Insert the site to the database
                        using (DbCommand cmd04 = conn.CreateCommand())
                        {
                            Site site = series.Site;

                            cmd04.CommandText = sqlSaveSite;
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Code));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Name));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Latitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Longitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, spatialReferenceID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Elevation_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.VerticalDatum));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalX));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalY));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, localProjectionID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.PosAccuracy_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.State));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.County));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Comments));

                            siteIDResult = cmd04.ExecuteScalar();
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 3 Variable
                    //****************************************************************
                    Variable variable = series.Variable;

                    using (DbCommand cmd05 = conn.CreateCommand())
                    {
                        cmd05.CommandText = sqlVariable;
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                        cmd05.Parameters[0].Value = variable.Code;
                        cmd05.Parameters[1].Value = variable.DataType;
                        variableIDResult = cmd05.ExecuteScalar();
                        if (variableIDResult != null)
                        {
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    if (variableID == 0) //New variable needs to be created
                    {
                        using (DbCommand cmd06 = conn.CreateCommand())
                        {
                            cmd06.CommandText = sqlUnits;
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));

                            variableUnitsIDResult = cmd06.ExecuteScalar();
                            if (variableUnitsIDResult != null)
                            {
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }

                            cmd06.Parameters[0].Value = variable.TimeUnit.Name;
                            cmd06.Parameters[1].Value = variable.TimeUnit.UnitsType;
                            cmd06.Parameters[2].Value = variable.TimeUnit.Abbreviation;
                            timeUnitsIDResult = cmd06.ExecuteScalar();
                            if (timeUnitsIDResult != null)
                            {
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        if (variableUnitsID == 0)
                        {
                            //save the variable units
                            using (DbCommand cmd07 = conn.CreateCommand())
                            {
                                //Save the variable units
                                cmd07.CommandText = sqlSaveUnits;
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));
                                variableUnitsIDResult = cmd07.ExecuteScalar();
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }
                        }

                        if (timeUnitsID == 0)
                        {
                            //save the time units
                            using (DbCommand cmd08 = conn.CreateCommand())
                            {
                                //Save the time units
                                cmd08.CommandText = sqlSaveUnits;
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Name));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.UnitsType));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Abbreviation));
                                timeUnitsIDResult = cmd08.ExecuteScalar();
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        //Insert the variable to the database
                        using (DbCommand cmd09 = conn.CreateCommand())
                        {
                            cmd09.CommandText = sqlSaveVariable;
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Name));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Speciation));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, variableUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.SampleMedium));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.ValueType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsRegular));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsCategorical));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.TimeSupport));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, timeUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.GeneralCategory));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.NoDataValue));

                            variableIDResult = cmd09.ExecuteScalar();
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 4 Method
                    //****************************************************************
                    Method method = series.Method;

                    using (DbCommand cmd10 = conn.CreateCommand())
                    {
                        cmd10.CommandText = sqlMethod;
                        cmd10.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                        methodIDResult = cmd10.ExecuteScalar();
                        if (methodIDResult != null)
                        {
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    if (methodID == 0)
                    {
                        using (DbCommand cmd11 = conn.CreateCommand())
                        {
                            cmd11.CommandText = sqlSaveMethod;
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Link));
                            methodIDResult = cmd11.ExecuteScalar();
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 5 Quality Control Level
                    //****************************************************************
                    QualityControlLevel qc = series.QualityControlLevel;

                    using (DbCommand cmd12 = conn.CreateCommand())
                    {
                        cmd12.CommandText = sqlQuality;
                        cmd12.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                        qualityControlLevelIDResult = cmd12.ExecuteScalar();
                        if (qualityControlLevelIDResult != null)
                        {
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    if (qualityControlLevelID == 0)
                    {
                        using (DbCommand cmd13 = conn.CreateCommand())
                        {
                            cmd13.CommandText = sqlSaveQualityControl;
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Code));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Explanation));
                            qualityControlLevelIDResult = cmd13.ExecuteScalar();
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 6 Source
                    //****************************************************************
                    Source source = series.Source;

                    using (DbCommand cmd14 = conn.CreateCommand())
                    {
                        cmd14.CommandText = sqlSource;
                        cmd14.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                        sourceIDResult = cmd14.ExecuteScalar();
                        if (sourceIDResult != null)
                        {
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    if (sourceID == 0)
                    {
                        ISOMetadata isoMetadata = source.ISOMetadata;

                        using (DbCommand cmd15 = conn.CreateCommand())
                        {
                            cmd15.CommandText = sqlISOMetadata;
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                            isoMetadataIDResult = cmd15.ExecuteScalar();
                            if (isoMetadataIDResult != null)
                            {
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        if (isoMetadataID == 0)
                        {
                            using (DbCommand cmd16 = conn.CreateCommand())
                            {
                                cmd16.CommandText = sqlSaveISOMetadata;
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.TopicCategory));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Abstract));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.ProfileVersion));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                                isoMetadataIDResult = cmd16.ExecuteScalar();
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        using (DbCommand cmd17 = conn.CreateCommand())
                        {
                            cmd17.CommandText = sqlSaveSource;
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Description));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Link));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.ContactName));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Phone));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Email));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Address));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.City));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.State));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.Int32, source.ZipCode));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Citation));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadataID));
                            sourceIDResult = cmd17.ExecuteScalar();
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 7 Series
                    //****************************************************************
                    seriesIDResult = null;
                    using (DbCommand cmdSeries = conn.CreateCommand())
                    {
                        //To retrieve the BeginTime, EndTime and SeriesID of the existing series
                        cmdSeries.CommandText = sqlSeries;
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                        cmdSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));

                        DbDataReader reader = cmdSeries.ExecuteReader(CommandBehavior.SingleRow);
                        if (reader.HasRows)
                        {
                            //a series already exists:
                            seriesIDResult = reader[0];
                            try
                            {
                                beginTimeDb = Convert.ToDateTime(reader[1]);
                                beginTimeUtcDb = Convert.ToDateTime(reader[2]);
                                endTimeDb = Convert.ToDateTime(reader[3]);
                                endTimeUtcDb = Convert.ToDateTime(reader[4]);
                                valueCountDb = Convert.ToInt32(reader[5]);
                            }
                            catch { }
                            finally
                            {
                                reader.Close();
                                reader.Dispose();
                            }
                        }
                    }


                    if (seriesIDResult != null && beginTimeDb > DateTime.MinValue && endTimeDb > DateTime.MinValue)
                    {
                        //Case 1: Series Already Exists.
                        seriesAlreadyExists = true;
                        seriesID = Convert.ToInt32(seriesIDResult);

                        //If the series already exists, delete any values within the existing time range from the database.
                        //also remove all items associated with the data values (qualifiers, samples..)
                        using (DbCommand cmdDeleteValues = conn.CreateCommand())
                        {
                            cmdDeleteValues.CommandText = sqlDeleteValues;
                            cmdDeleteValues.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                            cmdDeleteValues.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTime));
                            cmdDeleteValues.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTime));
                            var deletedCount = cmdDeleteValues.ExecuteNonQuery();
                            valueCountDb -= deletedCount; // Correct valueCount
                        }
                    }
                    else
                    {
                        //Case 2: Series does not exist.
                        seriesAlreadyExists = false;
                        using (DbCommand cmd18 = conn.CreateCommand())
                        {
                            cmd18.CommandText = sqlSaveSeries;
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.IsCategorical));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTimeUTC));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTimeUTC));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, series.ValueCount));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.CreationDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.Subscribed));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.UpdateDateTime));
                            cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.LastCheckedDateTime));

                            seriesIDResult = cmd18.ExecuteScalar();
                            seriesID = Convert.ToInt32(seriesIDResult);
                        }
                    }

                    //**********************************************************************
                    //*** Data Value - related: Only if new data values are saved **********
                    //**********************************************************************
                    if (series.DataValueList.Count > 0)
                    {

                        //****************************************************************
                        //*** Step 8 Qualifier and Sample Lookup
                        //****************************************************************
                        foreach (DataValue val in series.DataValueList)
                        {
                            if (val.Qualifier != null)
                            {
                                if (!qualifierLookup.ContainsKey(val.Qualifier.Code))
                                {
                                    qualifierLookup.Add(val.Qualifier.Code, val.Qualifier);
                                }
                            }

                            if (val.Sample != null)
                            {
                                if (!sampleLookup.ContainsKey(val.Sample.LabSampleCode))
                                {
                                    sampleLookup.Add(val.Sample.LabSampleCode, val.Sample);
                                }
                            }
                            if (val.OffsetType != null)
                            {
                                if (!offsetLookup.ContainsKey(val.OffsetType.Description))
                                {
                                    offsetLookup.Add(val.OffsetType.Description, val.OffsetType);
                                }
                            }
                        }

                        //****************************************************************
                        //*** Step 9 Qualifiers
                        //****************************************************************
                        if (qualifierLookup.Count > 0)
                        {
                            using (DbCommand cmd19 = conn.CreateCommand())
                            {
                                cmd19.CommandText = sqlQualifier;
                                cmd19.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Qualifier qualifier in qualifierLookup.Values)
                                {
                                    cmd19.Parameters[0].Value = qualifier.Code;
                                    qualifierIDResult = cmd19.ExecuteScalar();
                                    if (qualifierIDResult != null)
                                    {
                                        qualifier.Id = Convert.ToInt32(qualifierIDResult);
                                    }
                                }
                            }

                            List<Qualifier> unsavedQualifiers = new List<Qualifier>();
                            foreach (Qualifier qual in qualifierLookup.Values)
                            {
                                if (qual.Id == 0)
                                {
                                    unsavedQualifiers.Add(qual);
                                }
                            }

                            if (unsavedQualifiers.Count > 0)
                            {
                                using (DbCommand cmd20 = conn.CreateCommand())
                                {
                                    cmd20.CommandText = sqlSaveQualifier;
                                    cmd20.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd20.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (Qualifier qual2 in unsavedQualifiers)
                                    {
                                        cmd20.Parameters[0].Value = qual2.Code;
                                        cmd20.Parameters[1].Value = qual2.Description;
                                        qualifierIDResult = cmd20.ExecuteScalar();
                                        qual2.Id = Convert.ToInt32(qualifierIDResult);
                                    }
                                }
                            }
                        }

                        //****************************************************************
                        //*** TODO Step 10 Samples and Lab Methods
                        //****************************************************************
                        if (sampleLookup.Count > 0)
                        {
                            Dictionary<string, LabMethod> labMethodLookup = new Dictionary<string, LabMethod>();

                            using (DbCommand cmd21 = conn.CreateCommand())
                            {
                                cmd21.CommandText = sqlSample;
                                cmd21.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd21.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Sample sample in sampleLookup.Values)
                                {
                                    cmd21.Parameters[0].Value = sample.SampleType;
                                    cmd21.Parameters[1].Value = sample.LabSampleCode;
                                    sampleIDResult = cmd21.ExecuteScalar();
                                    if (sampleIDResult != null)
                                    {
                                        sample.Id = Convert.ToInt32(sampleIDResult);
                                    }
                                }
                            }


                            List<Sample> unsavedSamples = new List<Sample>();
                            List<LabMethod> unsavedLabMethods = new List<LabMethod>();

                            foreach (Sample samp in sampleLookup.Values)
                            {
                                if (samp.Id == 0)
                                {
                                    unsavedSamples.Add(samp);
                                    string labMethodKey = samp.LabMethod.LabName + "|" + samp.LabMethod.LabMethodName;
                                    if (!labMethodLookup.ContainsKey(labMethodKey))
                                    {
                                        labMethodLookup.Add(labMethodKey, samp.LabMethod);
                                    }
                                }
                            }

                            using (DbCommand cmd22 = conn.CreateCommand())
                            {
                                cmd22.CommandText = sqlLabMethod;
                                cmd22.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd22.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (LabMethod labMethod in labMethodLookup.Values)
                                {
                                    cmd22.Parameters[0].Value = labMethod.LabName;
                                    cmd22.Parameters[1].Value = labMethod.LabMethodName;
                                    labMethodIDResult = cmd22.ExecuteScalar();
                                    if (labMethodIDResult != null)
                                    {
                                        labMethod.Id = Convert.ToInt32(labMethodIDResult);
                                    }
                                }
                            }

                            //check unsaved lab methods
                            foreach (LabMethod lm in labMethodLookup.Values)
                            {
                                if (lm.Id == 0)
                                {
                                    unsavedLabMethods.Add(lm);
                                }
                            }

                            //save lab methods
                            if (unsavedLabMethods.Count > 0)
                            {
                                using (DbCommand cmd23 = conn.CreateCommand())
                                {
                                    cmd23.CommandText = sqlSaveLabMethod;
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd23.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (LabMethod labMethodToSave in unsavedLabMethods)
                                    {
                                        cmd23.Parameters[0].Value = labMethodToSave.LabName;
                                        cmd23.Parameters[1].Value = labMethodToSave.LabOrganization;
                                        cmd23.Parameters[2].Value = labMethodToSave.LabMethodName;
                                        cmd23.Parameters[3].Value = labMethodToSave.LabMethodLink;
                                        cmd23.Parameters[4].Value = labMethodToSave.LabMethodDescription;
                                        labMethodIDResult = cmd23.ExecuteScalar();
                                        labMethodToSave.Id = Convert.ToInt32(labMethodIDResult);
                                    }
                                }
                            }

                            //save samples
                            if (unsavedSamples.Count > 0)
                            {
                                using (DbCommand cmd24 = conn.CreateCommand())
                                {
                                    cmd24.CommandText = sqlSaveSample;
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32));

                                    foreach (Sample samp3 in unsavedSamples)
                                    {
                                        cmd24.Parameters[0].Value = samp3.SampleType;
                                        cmd24.Parameters[1].Value = samp3.LabSampleCode;
                                        cmd24.Parameters[2].Value = samp3.LabMethod.Id;
                                        sampleIDResult = cmd24.ExecuteScalar();
                                        samp3.Id = Convert.ToInt32(sampleIDResult);
                                    }
                                }
                            }
                        }



                        //****************************************************************
                        //*** TODO Step 11 Vertical Offsets (NEEDS TESTING DATA - DCEW)
                        //****************************************************************
                        if (offsetLookup.Count > 0)
                        {
                            Dictionary<string, Unit> offsetUnitLookup = new Dictionary<string, Unit>();
                            List<Unit> unsavedOffsetUnits = new List<Unit>();

                            using (DbCommand cmd25 = conn.CreateCommand())
                            {
                                cmd25.CommandText = sqlOffsetType;
                                cmd25.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (OffsetType offset in offsetLookup.Values)
                                {
                                    cmd25.Parameters[0].Value = offset.Description;
                                    offsetTypeIDResult = cmd25.ExecuteScalar();
                                    if (offsetTypeIDResult != null)
                                    {
                                        offset.Id = Convert.ToInt32(offsetTypeIDResult);
                                    }
                                }
                            }

                            //check unsaved offsets
                            List<OffsetType> unsavedoffsets = new List<OffsetType>();
                            foreach (OffsetType offset2 in offsetLookup.Values)
                            {
                                if (offset2.Id == 0)
                                {
                                    unsavedoffsets.Add(offset2);
                                    string offsetUnitsKey = offset2.Unit.Abbreviation + "|" + offset2.Unit.Name;
                                    if (!offsetUnitLookup.ContainsKey(offsetUnitsKey))
                                    {
                                        offsetUnitLookup.Add(offsetUnitsKey, offset2.Unit);
                                    }
                                }
                            }

                            //check for existing offset units
                            using (DbCommand cmd26 = conn.CreateCommand())
                            {
                                cmd26.CommandText = sqlUnits;
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd26.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Unit offsetUnit in offsetUnitLookup.Values)
                                {
                                    cmd26.Parameters[0].Value = offsetUnit.Name;
                                    cmd26.Parameters[1].Value = offsetUnit.UnitsType;
                                    cmd26.Parameters[2].Value = offsetUnit.Abbreviation;
                                    offsetUnitIDResult = cmd26.ExecuteScalar();
                                    if (offsetUnitIDResult != null)
                                    {
                                        offsetUnit.Id = Convert.ToInt32(offsetUnitIDResult);
                                    }
                                }
                            }

                            //check unsaved offset unit
                            foreach (Unit offsetUnit1 in offsetUnitLookup.Values)
                            {
                                if (offsetUnit1.Id == 0)
                                {
                                    unsavedOffsetUnits.Add(offsetUnit1);
                                }
                            }

                            //save offset units
                            if (unsavedOffsetUnits.Count > 0)
                            {
                                using (DbCommand cmd27 = conn.CreateCommand())
                                {
                                    cmd27.CommandText = sqlSaveUnits;
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                    cmd27.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (Unit unitToSave in unsavedOffsetUnits)
                                    {
                                        cmd27.Parameters[0].Value = unitToSave.Name;
                                        cmd27.Parameters[1].Value = unitToSave.UnitsType;
                                        cmd27.Parameters[2].Value = unitToSave.Abbreviation;

                                        offsetUnitIDResult = cmd27.ExecuteScalar();
                                        unitToSave.Id = Convert.ToInt32(offsetUnitIDResult);
                                    }
                                }
                            }

                            //save offset types
                            if (unsavedoffsets.Count > 0)
                            {
                                using (DbCommand cmd28 = conn.CreateCommand())
                                {
                                    cmd28.CommandText = sqlSaveOffsetType;
                                    cmd28.Parameters.Add(_db.CreateParameter(DbType.Int32));
                                    cmd28.Parameters.Add(_db.CreateParameter(DbType.String));

                                    foreach (OffsetType offsetToSave in unsavedoffsets)
                                    {
                                        cmd28.Parameters[0].Value = offsetToSave.Unit.Id;
                                        cmd28.Parameters[1].Value = offsetToSave.Description;
                                        offsetTypeIDResult = cmd28.ExecuteScalar();
                                        offsetToSave.Id = Convert.ToInt32(offsetTypeIDResult);
                                    }
                                }
                            }
                        }

                        //****************************************************************
                        //*** TODO Step 12 Data File - QueryInfo - DataService ***********
                        //****************************************************************

                        //****************************************************************
                        //*** TODO Step 13 Data Values related information     ***********
                        //****************************************************************

                        using (DbCommand cmd30 = conn.CreateCommand())
                        {
                            cmd30.CommandText = sqlSaveDataValue;
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                            cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));

                            foreach (DataValue val in series.DataValueList)
                            {
                                cmd30.Parameters[1].Value = val.Value;
                                cmd30.Parameters[2].Value = null;
                                if (val.ValueAccuracy != 0)
                                {
                                    cmd30.Parameters[2].Value = val.ValueAccuracy;
                                }
                                cmd30.Parameters[3].Value = val.LocalDateTime;
                                cmd30.Parameters[4].Value = val.UTCOffset;
                                cmd30.Parameters[5].Value = val.DateTimeUTC;
                                if (val.OffsetType != null)
                                {
                                    cmd30.Parameters[6].Value = val.OffsetValue;
                                    cmd30.Parameters[7].Value = val.OffsetType.Id;
                                }
                                else
                                {
                                    cmd30.Parameters[6].Value = null;
                                    cmd30.Parameters[7].Value = null;
                                }
                                cmd30.Parameters[8].Value = val.CensorCode;
                                if (val.Qualifier != null)
                                {
                                    cmd30.Parameters[9].Value = val.Qualifier.Id;
                                }

                                if (val.Sample != null)
                                {
                                    cmd30.Parameters[10].Value = val.Sample.Id;
                                }

                                cmd30.Parameters[11].Value = null; //TODO Check Data File

                                cmd30.ExecuteNonQuery();
                                numSavedValues++;
                            }
                        }

                        //****************************************************************
                        //*** Step 14 Data Series Update                       ***********
                        //****************************************************************
                        if (seriesAlreadyExists == true && seriesID > 0)
                        {
                            //begin DateTime
                            DateTime beginDateTime = beginTimeDb;
                            DateTime beginDateTimeUTC = beginTimeDb;
                            if (series.BeginDateTime < beginTimeDb)
                            {
                                beginDateTime = series.BeginDateTime;
                                beginDateTimeUTC = series.BeginDateTimeUTC;
                            }

                            //end DateTime
                            DateTime endDateTime = endTimeDb;
                            DateTime endDateTimeUTC = endTimeDb;
                            if (series.EndDateTime > endTimeDb)
                            {
                                endDateTime = series.EndDateTime;
                                endDateTimeUTC = series.EndDateTimeUTC;
                            }

                            //valueCount and UpdateDateTime
                            int valueCount = valueCountDb + series.ValueCount;
                            DateTime updateDateTime = DateTime.Now;

                            using (DbCommand cmdUpdateSeries = conn.CreateCommand())
                            {
                                cmdUpdateSeries.CommandText = sqlUpdateSeries;
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, beginDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, beginDateTimeUTC));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, endDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, endDateTimeUTC));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, valueCount));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.DateTime, updateDateTime));
                                cmdUpdateSeries.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));

                                cmdUpdateSeries.ExecuteNonQuery();
                            }
                        }
                    } //End of (If DataValueList.Count > 0)

                    //****************************************************************
                    //*** Step 15 Data Theme                               ***********
                    //****************************************************************


                    using (DbCommand cmd22 = conn.CreateCommand())
                    {
                        cmd22.CommandText = sqlTheme;
                        cmd22.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                        themeIDResult = cmd22.ExecuteScalar();
                        if (themeIDResult != null)
                        {
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    if (themeID == 0)
                    {
                        using (DbCommand cmd23 = conn.CreateCommand())
                        {
                            cmd23.CommandText = sqlSaveTheme1;
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Description));
                            themeIDResult = cmd23.ExecuteScalar();
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    //To save the Theme-Series combination (DataThemes DataTable)
                    object seriesThemeCombinationResult = null;
                    using (DbCommand cmd24 = conn.CreateCommand())
                    {
                        cmd24.CommandText = sqlThemeSeries;
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        seriesThemeCombinationResult = cmd24.ExecuteScalar();
                    }

                    if (seriesThemeCombinationResult == null)
                    {
                        using (DbCommand cmd25 = conn.CreateCommand())
                        {
                            cmd25.CommandText = sqlSaveTheme2;
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                            cmd25.ExecuteNonQuery();
                        }
                    }


                    //Step 13 Commit Transaction
                    tran.Commit();
                }
                conn.Close();
            }
            return numSavedValues;
        }

        /// <summary>
        /// Saves a data series to the database. The series will be associated with the 
        /// specified theme. This method does not check whether there are any existing series with 
        /// the same properties in the database. It will always create a new 'copy' of the series
        /// </summary>
        /// <param name="series">The time series</param>
        /// <param name="theme">The associated theme</param>
        /// <returns>Number of DataValue saved</returns>
        public int SaveSeriesAsCopy(Series series, Theme theme)
        {
            string sqlSite = "SELECT SiteID FROM Sites WHERE SiteCode = ?";
            string sqlVariable = "SELECT VariableID FROM Variables WHERE VariableCode = ? AND DataType = ?";
            string sqlSpatialReference = "SELECT SpatialReferenceID FROM SpatialReferences WHERE SRSID = ? AND SRSName = ?";
            string sqlUnits = "SELECT UnitsID FROM Units WHERE UnitsName = ? AND UnitsType = ? AND UnitsAbbreviation = ?";
            string sqlMethod = "SELECT MethodID FROM Methods WHERE MethodDescription = ?";
            string sqlSource = "SELECT SourceID FROM Sources WHERE Organization = ?";
            string sqlISOMetadata = "SELECT MetadataID FROM ISOMetadata WHERE Title = ? AND MetadataLink = ?";
            string sqlQuality = "SELECT QualityControlLevelID FROM QualityControlLevels WHERE Definition = ?";
            string sqlQualifier = "SELECT QualifierID FROM Qualifiers WHERE QualifierCode = ?";
            string sqlSample = "SELECT SampleID FROM Samples WHERE SampleType = ? AND LabSampleCode = ?";
            string sqlLabMethod = "SELECT LabMethodID FROM LabMethods WHERE LabName = ? AND LabMethodName = ?";
            string sqlOffsetType = "SELECT OffsetTypeID FROM OffsetTypes WHERE OffsetDescription = ?";
            string sqlTheme = "SELECT ThemeID FROM DataThemeDescriptions WHERE ThemeName = ?";
            string sqlRowID = "; SELECT LAST_INSERT_ROWID();";
            //string sqlSeries = "SELECT SeriesID FROM DataSeries WHERE SiteID = ? AND VariableID = ? AND MethodID = ? AND QualityControlLevelID = ? AND SourceID = ?";

            string sqlSaveSpatialReference = "INSERT INTO SpatialReferences(SRSID, SRSName) VALUES(?, ?)" + sqlRowID;

            string sqlSaveSite = "INSERT INTO Sites(SiteCode, SiteName, Latitude, Longitude, LatLongDatumID, Elevation_m, VerticalDatum, " +
                                                   "LocalX, LocalY, LocalProjectionID, PosAccuracy_m, State, County, Comments) " +
                                                   "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveUnits = "INSERT INTO Units(UnitsName, UnitsType, UnitsAbbreviation) VALUES(?, ?, ?)" + sqlRowID;

            string sqlSaveVariable = "INSERT INTO Variables(VariableCode, VariableName, Speciation, VariableUnitsID, SampleMedium, ValueType, " +
                "IsRegular, ISCategorical, TimeSupport, TimeUnitsID, DataType, GeneralCategory, NoDataValue) " +
                "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveMethod = "INSERT INTO Methods(MethodDescription, MethodLink) VALUES(?, ?)" + sqlRowID;

            string sqlSaveQualityControl = "INSERT INTO QualityControlLevels(QualityControlLevelCode, Definition, Explanation) " +
                "VALUES(?,?,?)" + sqlRowID;

            string sqlSaveSource = "INSERT INTO Sources(Organization, SourceDescription, SourceLink, ContactName, Phone, " +
                                   "Email, Address, City, State, ZipCode, Citation, MetadataID) " +
                                   "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveISOMetadata = "INSERT INTO ISOMetadata(TopicCategory, Title, Abstract, ProfileVersion, MetadataLink) " +
                                    "VALUES(?,?,?,?,?)" + sqlRowID;

            string sqlSaveSeries = "INSERT INTO DataSeries(SiteID, VariableID, MethodID, SourceID, QualityControlLevelID, " +
                "IsCategorical, BeginDateTime, EndDateTime, BeginDateTimeUTC, EndDateTimeUTC, ValueCount, CreationDateTime, " +
                "Subscribed, UpdateDateTime, LastCheckedDateTime) " +
                "VALUES(?, ?, ?, ?,?,?,?,?,?,?,?,?,?,?,?)" + sqlRowID;

            string sqlSaveQualifier = "INSERT INTO Qualifiers(QualifierCode, QualifierDescription) VALUES (?,?)" + sqlRowID;

            string sqlSaveSample = "INSERT INTO Samples(SampleType, LabSampleCode, LabMethodID) VALUES (?,?, ?)" + sqlRowID;

            string sqlSaveLabMethod = "INSERT INTO LabMethods(LabName, LabOrganization, LabMethodName, LabMethodLink, LabMethodDescription) " +
                "VALUES(?, ?, ?, ?, ?)" + sqlRowID;

            string sqlSaveOffsetType = "INSERT INTO OffsetTypes(OffsetUnitsID, OffsetDescription) VALUES (?, ?)" + sqlRowID;

            string sqlSaveDataValue = "INSERT INTO DataValues(SeriesID, DataValue, ValueAccuracy, LocalDateTime, " +
                "UTCOffset, DateTimeUTC, OffsetValue, OffsetTypeID, CensorCode, QualifierID, SampleID, FileID) " +
                "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)";

            string sqlSaveTheme1 = "INSERT INTO DataThemeDescriptions(ThemeName, ThemeDescription) VALUES (?,?)" + sqlRowID;
            string sqlSaveTheme2 = "INSERT INTO DataThemes(ThemeID,SeriesID) VALUEs (?,?)";

            int siteID = 0;
            int variableID = 0;
            int spatialReferenceID = 0;
            int localProjectionID = 0;
            int variableUnitsID = 0;
            int timeUnitsID = 0;
            int methodID = 0;
            int qualityControlLevelID = 0;
            int sourceID = 0;
            int isoMetadataID = 0;
            int seriesID = 0;
            int themeID = 0;
            //int offsetTypeID = 0;
            
            object siteIDResult = null;
            object spatialReferenceIDResult = null;
            object localProjectionIDResult = null;
            object variableIDResult = null;
            object variableUnitsIDResult = null;
            object timeUnitsIDResult = null;
            object methodIDResult = null;
            object qualityControlLevelIDResult = null;
            object sourceIDResult = null;
            object isoMetadataIDResult = null;
            object seriesIDResult = null;
            object qualifierIDResult = null;
            object themeIDResult = null;
            object sampleIDResult = null;
            object labMethodIDResult = null;
            object offsetTypeIDResult = null;
            object offsetUnitIDResult = null;

            Dictionary<string, Qualifier> qualifierLookup = new Dictionary<string, Qualifier>();
            Dictionary<string, Sample> sampleLookup = new Dictionary<string, Sample>();
            Dictionary<string, OffsetType> offsetLookup = new Dictionary<string, OffsetType>();

            int numSavedValues = 0;
            
            //Step 1 Begin Transaction
            using (DbConnection conn = _db.CreateConnection())
            {
                conn.Open();
                
                using (DbTransaction tran = conn.BeginTransaction())
                {
                    //****************************************************************
                    //*** Step 2 Site
                    //****************************************************************
                    using (DbCommand cmd01 = conn.CreateCommand())
                    {
                        cmd01.CommandText = sqlSite;
                        cmd01.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.Code));
                        siteIDResult = cmd01.ExecuteScalar();
                        if (siteIDResult != null)
                        {
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    if (siteID == 0) //New Site needs to be created
                    {
                        using (DbCommand cmd02 = conn.CreateCommand())
                        {
                            cmd02.CommandText = sqlSpatialReference;
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                            cmd02.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));
                            
                            spatialReferenceIDResult = cmd02.ExecuteScalar();
                            if (spatialReferenceIDResult != null)
                            {
                                spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                            }
                            
                            if (series.Site.LocalProjection != null)
                            {
                                cmd02.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                cmd02.Parameters[1].Value = series.Site.LocalProjection.SRSName;

                                localProjectionIDResult = cmd02.ExecuteScalar();
                                if (localProjectionIDResult != null)
                                {
                                    localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                }
                            }
                        }

                        if (spatialReferenceID == 0)
                        {
                            //save spatial reference and the local projection
                            using (DbCommand cmd03 = conn.CreateCommand())
                            {
                                //Save the spatial reference (Lat / Long Datum)
                                cmd03.CommandText = sqlSaveSpatialReference;
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.Int32, series.Site.SpatialReference.SRSID));
                                cmd03.Parameters.Add(_db.CreateParameter(DbType.String, series.Site.SpatialReference.SRSName));
                                
                                spatialReferenceIDResult = cmd03.ExecuteScalar();

                                if (spatialReferenceIDResult != null)
                                {
                                    spatialReferenceID = Convert.ToInt32(spatialReferenceIDResult);
                                }

                                //Save the local projection
                                if (series.Site.LocalProjection != null)
                                {
                                    if (localProjectionID == 0)
                                    {
                                        cmd03.Parameters[0].Value = series.Site.LocalProjection.SRSID;
                                        cmd03.Parameters[1].Value = series.Site.LocalProjection.SRSName;
                                        localProjectionIDResult = cmd03.ExecuteScalar();
                                        localProjectionID = Convert.ToInt32(localProjectionIDResult);
                                    }
                                }
                            }
                        }

                        //Insert the site to the database
                        using (DbCommand cmd04 = conn.CreateCommand())
                        {
                            Site site = series.Site;

                            cmd04.CommandText = sqlSaveSite;
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Code));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Name));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Latitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Longitude));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, spatialReferenceID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.Elevation_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.VerticalDatum));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalX));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.LocalY));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Int32, localProjectionID));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.Double, site.PosAccuracy_m));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.State));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.County));
                            cmd04.Parameters.Add(_db.CreateParameter(DbType.String, site.Comments));

                            siteIDResult = cmd04.ExecuteScalar();
                            siteID = Convert.ToInt32(siteIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 3 Variable
                    //****************************************************************
                    Variable variable = series.Variable;

                    using (DbCommand cmd05 = conn.CreateCommand())
                    {
                        cmd05.CommandText = sqlVariable;
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                        cmd05.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                        cmd05.Parameters[0].Value = variable.Code;
                        cmd05.Parameters[1].Value = variable.DataType;
                        variableIDResult = cmd05.ExecuteScalar();
                        if (variableIDResult != null)
                        {
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    if (variableID == 0) //New variable needs to be created
                    {
                        using (DbCommand cmd06 = conn.CreateCommand())
                        {
                            cmd06.CommandText = sqlUnits;
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                            cmd06.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));
                            
                            variableUnitsIDResult = cmd06.ExecuteScalar();
                            if (variableUnitsIDResult != null)
                            {
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }

                            cmd06.Parameters[0].Value = variable.TimeUnit.Name;
                            cmd06.Parameters[1].Value = variable.TimeUnit.UnitsType;
                            cmd06.Parameters[2].Value = variable.TimeUnit.Abbreviation;
                            timeUnitsIDResult = cmd06.ExecuteScalar();
                            if (timeUnitsIDResult != null)
                            {
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        if (variableUnitsID == 0)
                        {
                            //save the variable units
                            using (DbCommand cmd07 = conn.CreateCommand())
                            {
                                //Save the variable units
                                cmd07.CommandText = sqlSaveUnits;
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Name));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.UnitsType));
                                cmd07.Parameters.Add(_db.CreateParameter(DbType.String, variable.VariableUnit.Abbreviation));
                                variableUnitsIDResult = cmd07.ExecuteScalar();
                                variableUnitsID = Convert.ToInt32(variableUnitsIDResult);
                            }
                        }

                        if (timeUnitsID == 0)
                        {
                            //save the time units
                            using (DbCommand cmd08 = conn.CreateCommand())
                            {
                                //Save the time units
                                cmd08.CommandText = sqlSaveUnits;
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Name));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.UnitsType));
                                cmd08.Parameters.Add(_db.CreateParameter(DbType.String, variable.TimeUnit.Abbreviation));
                                timeUnitsIDResult = cmd08.ExecuteScalar();
                                timeUnitsID = Convert.ToInt32(timeUnitsIDResult);
                            }
                        }

                        //Insert the variable to the database
                        using (DbCommand cmd09 = conn.CreateCommand())
                        {
                            cmd09.CommandText = sqlSaveVariable;
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Code));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Name));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.Speciation));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, variableUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.SampleMedium));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.ValueType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsRegular));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Boolean, variable.IsCategorical));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.TimeSupport));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Int32, timeUnitsID));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.DataType));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.String, variable.GeneralCategory));
                            cmd09.Parameters.Add(_db.CreateParameter(DbType.Double, variable.NoDataValue));

                            variableIDResult = cmd09.ExecuteScalar();
                            variableID = Convert.ToInt32(variableIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 4 Method
                    //****************************************************************
                    Method method = series.Method;

                    using (DbCommand cmd10 = conn.CreateCommand())
                    {
                        cmd10.CommandText = sqlMethod;
                        cmd10.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                        methodIDResult = cmd10.ExecuteScalar();
                        if (methodIDResult != null)
                        {
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    if (methodID == 0)
                    {
                        using (DbCommand cmd11 = conn.CreateCommand())
                        {
                            cmd11.CommandText = sqlSaveMethod;
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Description));
                            cmd11.Parameters.Add(_db.CreateParameter(DbType.String, method.Link));
                            methodIDResult = cmd11.ExecuteScalar();
                            methodID = Convert.ToInt32(methodIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 5 Quality Control Level
                    //****************************************************************
                    QualityControlLevel qc = series.QualityControlLevel;

                    using (DbCommand cmd12 = conn.CreateCommand())
                    {
                        cmd12.CommandText = sqlQuality;
                        cmd12.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                        qualityControlLevelIDResult = cmd12.ExecuteScalar();
                        if (qualityControlLevelIDResult != null)
                        {
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    if (qualityControlLevelID == 0)
                    {
                        using (DbCommand cmd13 = conn.CreateCommand())
                        {
                            cmd13.CommandText = sqlSaveQualityControl;
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Code));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Definition));
                            cmd13.Parameters.Add(_db.CreateParameter(DbType.String, qc.Explanation));
                            qualityControlLevelIDResult = cmd13.ExecuteScalar();
                            qualityControlLevelID = Convert.ToInt32(qualityControlLevelIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 6 Source
                    //****************************************************************
                    Source source = series.Source;

                    using (DbCommand cmd14 = conn.CreateCommand())
                    {
                        cmd14.CommandText = sqlSource;
                        cmd14.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                        sourceIDResult = cmd14.ExecuteScalar();
                        if (sourceIDResult != null)
                        {
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    if (sourceID == 0)
                    {
                        ISOMetadata isoMetadata = source.ISOMetadata;
                        
                        using (DbCommand cmd15 = conn.CreateCommand())
                        {
                            cmd15.CommandText = sqlISOMetadata;
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                            cmd15.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                            isoMetadataIDResult = cmd15.ExecuteScalar();
                            if (isoMetadataIDResult != null)
                            {
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }

                        if (isoMetadataID == 0)
                        {
                            using (DbCommand cmd16 = conn.CreateCommand())
                            {
                                cmd16.CommandText = sqlSaveISOMetadata;
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.TopicCategory));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Title));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.Abstract));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.ProfileVersion));
                                cmd16.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadata.MetadataLink));
                                isoMetadataIDResult = cmd16.ExecuteScalar();
                                isoMetadataID = Convert.ToInt32(isoMetadataIDResult);
                            }
                        }
                        
                        using (DbCommand cmd17 = conn.CreateCommand())
                        {
                            cmd17.CommandText = sqlSaveSource;
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Organization));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Description));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Link));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.ContactName));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Phone));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Email));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Address));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.City));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.State));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.Int32, source.ZipCode));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, source.Citation));
                            cmd17.Parameters.Add(_db.CreateParameter(DbType.String, isoMetadataID));
                            sourceIDResult = cmd17.ExecuteScalar();
                            sourceID = Convert.ToInt32(sourceIDResult);
                        }
                    }

                    //****************************************************************
                    //*** Step 7 Series
                    //****************************************************************
                    using (DbCommand cmd18 = conn.CreateCommand())
                    {
                        cmd18.CommandText = sqlSaveSeries;
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, siteID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, variableID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, methodID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, sourceID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, qualityControlLevelID));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.IsCategorical));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.BeginDateTimeUTC));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.EndDateTimeUTC));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Int32, series.ValueCount));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.CreationDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.Boolean, series.Subscribed));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.UpdateDateTime));
                        cmd18.Parameters.Add(_db.CreateParameter(DbType.DateTime, series.LastCheckedDateTime));

                        seriesIDResult = cmd18.ExecuteScalar();
                        seriesID = Convert.ToInt32(seriesIDResult);
                    }


                    //****************************************************************
                    //*** Step 8 Qualifier and Sample Lookup
                    //****************************************************************
                    foreach (DataValue val in series.DataValueList)
                    {
                        if (val.Qualifier != null)
                        {
                            if (!qualifierLookup.ContainsKey(val.Qualifier.Code))
                            {
                                qualifierLookup.Add(val.Qualifier.Code, val.Qualifier);
                            }
                        }

                        if (val.Sample != null)
                        {
                            if (!sampleLookup.ContainsKey(val.Sample.LabSampleCode))
                            {
                                sampleLookup.Add(val.Sample.LabSampleCode, val.Sample);
                            }
                        }
                        if (val.OffsetType != null)
                        {
                            if (!offsetLookup.ContainsKey(val.OffsetType.Description))
                            {
                                offsetLookup.Add(val.OffsetType.Description, val.OffsetType);
                            }
                        }
                    }

                    //****************************************************************
                    //*** Step 9 Qualifiers
                    //****************************************************************
                    if (qualifierLookup.Count > 0)
                    {
                        using (DbCommand cmd19 = conn.CreateCommand())
                        {
                            cmd19.CommandText = sqlQualifier;
                            cmd19.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Qualifier qualifier in qualifierLookup.Values)
                            {
                                cmd19.Parameters[0].Value = qualifier.Code;
                                qualifierIDResult = cmd19.ExecuteScalar();
                                if (qualifierIDResult != null)
                                {
                                    qualifier.Id = Convert.ToInt32(qualifierIDResult);
                                }
                            }
                        }

                        List<Qualifier> unsavedQualifiers = new List<Qualifier>();
                        foreach (Qualifier qual in qualifierLookup.Values)
                        {
                            if (qual.Id == 0)
                            {
                                unsavedQualifiers.Add(qual);
                            }
                        }

                        if (unsavedQualifiers.Count > 0)
                        {
                            using (DbCommand cmd20 = conn.CreateCommand())
                            {
                                cmd20.CommandText = sqlSaveQualifier;
                                cmd20.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd20.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Qualifier qual2 in unsavedQualifiers)
                                {
                                    cmd20.Parameters[0].Value = qual2.Code;
                                    cmd20.Parameters[1].Value = qual2.Description;
                                    qualifierIDResult = cmd20.ExecuteScalar();
                                    qual2.Id = Convert.ToInt32(qualifierIDResult);
                                }
                            }
                        }
                    }

                    //****************************************************************
                    //*** TODO Step 10 Samples and Lab Methods
                    //****************************************************************
                    if (sampleLookup.Count > 0)
                    {
                        Dictionary<string, LabMethod> labMethodLookup = new Dictionary<string, LabMethod>();

                        using (DbCommand cmd21 = conn.CreateCommand())
                        {
                            cmd21.CommandText = sqlSample;
                            cmd21.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd21.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Sample sample in sampleLookup.Values)
                            {
                                cmd21.Parameters[0].Value = sample.SampleType;
                                cmd21.Parameters[1].Value = sample.LabSampleCode;
                                sampleIDResult = cmd21.ExecuteScalar();
                                if (sampleIDResult != null)
                                {
                                    sample.Id = Convert.ToInt32(sampleIDResult);
                                }
                            }
                        }


                        List<Sample> unsavedSamples = new List<Sample>();
                        List<LabMethod> unsavedLabMethods = new List<LabMethod>();
                        
                        foreach (Sample samp in sampleLookup.Values)
                        {
                            if (samp.Id == 0)
                            {
                                unsavedSamples.Add(samp);
                                string labMethodKey = samp.LabMethod.LabName + "|" + samp.LabMethod.LabMethodName;
                                if (! labMethodLookup.ContainsKey(labMethodKey))
                                {
                                    labMethodLookup.Add(labMethodKey, samp.LabMethod);
                                }
                            }
                        }

                        using (DbCommand cmd22 = conn.CreateCommand())
                        {
                            cmd22.CommandText = sqlLabMethod;
                            cmd22.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd22.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (LabMethod labMethod in labMethodLookup.Values)
                            {
                                cmd22.Parameters[0].Value = labMethod.LabName;
                                cmd22.Parameters[1].Value = labMethod.LabMethodName;
                                labMethodIDResult = cmd22.ExecuteScalar();
                                if (labMethodIDResult != null)
                                {
                                    labMethod.Id = Convert.ToInt32(labMethodIDResult);
                                }
                            }
                        }

                        //check unsaved lab methods
                        foreach (LabMethod lm in labMethodLookup.Values)
                        {
                            if (lm.Id == 0)
                            {
                                unsavedLabMethods.Add(lm);
                            }
                        }

                        //save lab methods
                        if (unsavedLabMethods.Count > 0)
                        {
                            using (DbCommand cmd23 = conn.CreateCommand())
                            {
                                cmd23.CommandText = sqlSaveLabMethod;
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd23.Parameters.Add(_db.CreateParameter(DbType.String));
                            
                                foreach (LabMethod labMethodToSave in unsavedLabMethods)
                                {
                                    cmd23.Parameters[0].Value = labMethodToSave.LabName;
                                    cmd23.Parameters[1].Value = labMethodToSave.LabOrganization;
                                    cmd23.Parameters[2].Value = labMethodToSave.LabMethodName;
                                    cmd23.Parameters[3].Value = labMethodToSave.LabMethodLink;
                                    cmd23.Parameters[4].Value = labMethodToSave.LabMethodDescription;
                                    labMethodIDResult = cmd23.ExecuteScalar();
                                    labMethodToSave.Id = Convert.ToInt32(labMethodIDResult);
                                }
                            }
                        }

                        //save samples
                        if (unsavedSamples.Count > 0)
                        {
                            using (DbCommand cmd24 = conn.CreateCommand())
                            {
                                cmd24.CommandText = sqlSaveSample;
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32));

                                foreach (Sample samp3 in unsavedSamples)
                                {
                                    cmd24.Parameters[0].Value = samp3.SampleType;
                                    cmd24.Parameters[1].Value = samp3.LabSampleCode;
                                    cmd24.Parameters[2].Value = samp3.LabMethod.Id;
                                    sampleIDResult = cmd24.ExecuteScalar();
                                    samp3.Id = Convert.ToInt32(sampleIDResult);
                                }
                            }
                        }
                    }



                    //****************************************************************
                    //*** TODO Step 11 Vertical Offsets (NEEDS TESTING DATA - DCEW)
                    //****************************************************************
                    if (offsetLookup.Count > 0)
                    {
                        Dictionary<string, Unit> offsetUnitLookup = new Dictionary<string, Unit>();
                        List<Unit> unsavedOffsetUnits = new List<Unit>();
                        
                        using (DbCommand cmd25 = conn.CreateCommand())
                        {
                            cmd25.CommandText = sqlOffsetType;
                            cmd25.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (OffsetType offset in offsetLookup.Values)
                            {
                                cmd25.Parameters[0].Value = offset.Description;
                                offsetTypeIDResult = cmd25.ExecuteScalar();
                                if (offsetTypeIDResult != null)
                                {
                                    offset.Id = Convert.ToInt32(offsetTypeIDResult);
                                }
                            }
                        }

                        //check unsaved offsets
                        List<OffsetType> unsavedoffsets = new List<OffsetType>();
                        foreach (OffsetType offset2 in offsetLookup.Values)
                        {
                            if (offset2.Id == 0)
                            {
                                unsavedoffsets.Add(offset2);
                                string offsetUnitsKey =  offset2.Unit.Abbreviation + "|" + offset2.Unit.Name;
                                if (!offsetUnitLookup.ContainsKey(offsetUnitsKey))
                                {
                                    offsetUnitLookup.Add(offsetUnitsKey, offset2.Unit);
                                }
                            }
                        }

                        //check for existing offset units
                        using (DbCommand cmd26 = conn.CreateCommand())
                        {
                            cmd26.CommandText = sqlUnits;
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));
                            cmd26.Parameters.Add(_db.CreateParameter(DbType.String));

                            foreach (Unit offsetUnit in offsetUnitLookup.Values)
                            {
                                cmd26.Parameters[0].Value = offsetUnit.Name;
                                cmd26.Parameters[1].Value = offsetUnit.UnitsType;
                                cmd26.Parameters[2].Value = offsetUnit.Abbreviation;
                                offsetUnitIDResult = cmd26.ExecuteScalar();
                                if (offsetUnitIDResult != null)
                                {
                                    offsetUnit.Id = Convert.ToInt32(offsetUnitIDResult);
                                }
                            }
                        }

                        //check unsaved offset unit
                        foreach (Unit offsetUnit1 in offsetUnitLookup.Values)
                        {
                            if (offsetUnit1.Id == 0)
                            {
                                unsavedOffsetUnits.Add(offsetUnit1);
                            }
                        }

                        //save offset units
                        if (unsavedOffsetUnits.Count > 0)
                        {
                            using (DbCommand cmd27 = conn.CreateCommand())
                            {
                                cmd27.CommandText = sqlSaveUnits;
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));
                                cmd27.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (Unit unitToSave in unsavedOffsetUnits)
                                {
                                    cmd27.Parameters[0].Value = unitToSave.Name;
                                    cmd27.Parameters[1].Value = unitToSave.UnitsType;
                                    cmd27.Parameters[2].Value = unitToSave.Abbreviation;
                                    
                                    offsetUnitIDResult = cmd27.ExecuteScalar();
                                    unitToSave.Id = Convert.ToInt32(offsetUnitIDResult);
                                }
                            }
                        }

                        //save offset types
                        if (unsavedoffsets.Count > 0)
                        {
                            using (DbCommand cmd28 = conn.CreateCommand())
                            {
                                cmd28.CommandText = sqlSaveOffsetType;
                                cmd28.Parameters.Add(_db.CreateParameter(DbType.Int32));
                                cmd28.Parameters.Add(_db.CreateParameter(DbType.String));

                                foreach (OffsetType offsetToSave in unsavedoffsets)
                                {
                                    cmd28.Parameters[0].Value = offsetToSave.Unit.Id;
                                    cmd28.Parameters[1].Value = offsetToSave.Description;
                                    offsetTypeIDResult = cmd28.ExecuteScalar();
                                    offsetToSave.Id = Convert.ToInt32(offsetTypeIDResult);
                                }
                            }
                        }
                    }

                    //****************************************************************
                    //*** TODO Step 12 Data File - QueryInfo - DataService ***********
                    //****************************************************************

                    //****************************************************************
                    //*** TODO Step 13 Data Values                         ***********
                    //****************************************************************
                    using (DbCommand cmd30 = conn.CreateCommand())
                    {
                        cmd30.CommandText = sqlSaveDataValue;
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.DateTime));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Double));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.String));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));
                        cmd30.Parameters.Add(_db.CreateParameter(DbType.Int32));

                        foreach (DataValue val in series.DataValueList)
                        {
                            cmd30.Parameters[1].Value = val.Value;
                            cmd30.Parameters[2].Value = null;
                            if (val.ValueAccuracy != 0)
                            {
                                cmd30.Parameters[2].Value = val.ValueAccuracy;
                            }
                            cmd30.Parameters[3].Value = val.LocalDateTime;
                            cmd30.Parameters[4].Value = val.UTCOffset;
                            cmd30.Parameters[5].Value = val.DateTimeUTC;
                            if (val.OffsetType != null)
                            {
                                cmd30.Parameters[6].Value = val.OffsetValue;
                                cmd30.Parameters[7].Value = val.OffsetType.Id;
                            }
                            else
                            {
                                cmd30.Parameters[6].Value = null;
                                cmd30.Parameters[7].Value = null;
                            }
                            cmd30.Parameters[8].Value = val.CensorCode;
                            if (val.Qualifier != null)
                            {
                                cmd30.Parameters[9].Value = val.Qualifier.Id;
                            }

                            if (val.Sample != null)
                            {
                                cmd30.Parameters[10].Value = val.Sample.Id;
                            }
                            
                            cmd30.Parameters[11].Value = null; //TODO Check Data File

                            cmd30.ExecuteNonQuery();
                            numSavedValues++;
                        }
                    }

                    //****************************************************************
                    //*** Step 14 Data Theme                               ***********
                    //****************************************************************
                    using (DbCommand cmd22 = conn.CreateCommand())
                    {
                        cmd22.CommandText = sqlTheme;
                        cmd22.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                        themeIDResult = cmd22.ExecuteScalar();
                        if (themeIDResult != null)
                        {
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    if (themeID == 0)
                    {
                        using (DbCommand cmd23 = conn.CreateCommand())
                        {
                            cmd23.CommandText = sqlSaveTheme1;
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Name));
                            cmd23.Parameters.Add(_db.CreateParameter(DbType.String, theme.Description));
                            themeIDResult = cmd23.ExecuteScalar();
                            themeID = Convert.ToInt32(themeIDResult);
                        }
                    }

                    using (DbCommand cmd24 = conn.CreateCommand())
                    {
                        cmd24.CommandText = sqlSaveTheme2;
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, themeID));
                        cmd24.Parameters.Add(_db.CreateParameter(DbType.Int32, seriesID));
                        cmd24.ExecuteNonQuery();
                    }

                    //Step 13 Commit Transaction
                    tran.Commit();
                }
                conn.Close();
            }
            return numSavedValues;
        }
        #endregion

        #region Theme Management
        /// <summary>
        /// Saves a theme to the database including the association
        /// between any of its series.
        /// </summary>
        /// <param name="themeToSave">The theme to be saved</param>
        public void SaveTheme(Theme themeToSave)
        {
            throw new NotImplementedException();
           
        }

        /// <summary>
        /// Gets all themes from the database ordered by the theme name
        /// </summary>
        /// <returns>The list of all themes</returns>
        public IList<Theme> GetAllThemes()
        {
            string sql = "SELECT ThemeID, ThemeName, ThemeDescription FROM DataThemeDescriptions";
            DataTable table = _db.LoadTable("tblThemes", sql);
            
            if (table.Rows.Count == 0)
            {
                return new List<Theme>();
            }
            else
            {
                List<Theme> themeList = new List<Theme>();
                foreach(DataRow row in table.Rows)
                {
                    Theme newTheme = new Theme(row[1].ToString(), row[2].ToString());
                    newTheme.Id = Convert.ToInt32(row[0]);
                    themeList.Add(newTheme);
                }
                return themeList;
            }
        }
        #endregion

        #endregion

        #region Events
        public event EventHandler ThemeSaved;
        public event EventHandler ThemeDeleted;
        public event EventHandler SeriesAdded;
        #endregion


        #region Protected Methods
        protected void OnThemeSaved()
        {
            if (ThemeSaved != null) ThemeSaved(this, null);
        }

        protected void OnSeriesAdded()
        {
            if (SeriesAdded != null) SeriesAdded(this, null);
        }

        protected void OnThemeDeleted()
        {
            if (ThemeDeleted != null) ThemeDeleted(this, null);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks if the SQLite db file exists. if it doesn't exist,
        /// re-create it
        /// </summary>
        private void CheckDbFile(string sqLiteConnString)
        {
            string sqlitePath = SQLiteHelper.GetSQLiteFileName(sqLiteConnString);
            if (!SQLiteHelper.DatabaseExists(sqlitePath))
            {
                SQLiteHelper.CreateSQLiteDatabase(sqlitePath);
            }
        }

        #endregion
    }
}