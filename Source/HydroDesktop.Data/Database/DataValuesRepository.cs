using System;
using System.Data;
using HydroDesktop.Interfaces;

namespace HydroDesktop.Database
{
    /// <summary>
    /// Repository for DataValues
    /// </summary>
    class DataValuesRepository : BaseRepository, IDataValuesRepository
    {
        #region Constructors

        public DataValuesRepository(DatabaseTypes dbType, string connectionString)
            : base(dbType, connectionString)
        {
        }

        public DataValuesRepository(IHydroDbOperations db)
            : base(db)
        {
        }

        #endregion

        #region Public methods

        public double AggregateValues(long seriesID, string aggregateFunction, DateTime minDate, DateTime maxDate)
        {
            var query =
                string.Format(
                    "select {0}(DataValue) from {1} main " +
                    "LEFT JOIN DataSeries ds ON ds.SeriesID = main.SeriesID " +
                    "LEFT JOIN Variables v ON v.VariableID = ds.VariableID " +
                    "WHERE DateTimeUTC >= '{2}' and DateTimeUTC <= '{3}' and main.SeriesID = {4} and DataValue <> v.NoDataValue ",
                    aggregateFunction, TableName, 
                    minDate.ToString("yyyy-MM-dd HH:mm:ss"), maxDate.ToString("yyyy-MM-dd HH:mm:ss"), 
                    seriesID);
            var value =  Convert.ToDouble(DbOperations.ExecuteSingleOutput(query));
            return value;
        }

        public double CalculatePercAvailable(long seriesID, DateTime minDate, DateTime maxDate)
        {
            var query =
              string.Format(
                  "select count(DataValue) from {0} main " +
                  "LEFT JOIN DataSeries ds ON ds.SeriesID = main.SeriesID " +
                  "LEFT JOIN Variables v ON v.VariableID = ds.VariableID " +
                  "WHERE DateTimeUTC >= '{1}' and DateTimeUTC <= '{2}' and main.SeriesID = {3} and DataValue <> v.NoDataValue ",
                  TableName,
                  minDate.ToString("yyyy-MM-dd HH:mm:ss"), maxDate.ToString("yyyy-MM-dd HH:mm:ss"),
                  seriesID);
            var hasValues = Convert.ToDouble(DbOperations.ExecuteSingleOutput(query));

            query =
              string.Format(
                  "select count(DataValue) from {0} main " +
                  "WHERE DateTimeUTC >= '{1}' and DateTimeUTC <= '{2}' and main.SeriesID = {3} ",
                  TableName,
                  minDate.ToString("yyyy-MM-dd HH:mm:ss"), maxDate.ToString("yyyy-MM-dd HH:mm:ss"),
                  seriesID);
            var totalValues = Convert.ToDouble(DbOperations.ExecuteSingleOutput(query));

            var value = hasValues*100.0/totalValues;
            return value;
        }

        public DataTable GetAll(long seriesID)
        {
            var query = "SELECT * FROM DataValues WHERE SeriesID = " + seriesID;
            var result = DbOperations.LoadTable(TableName, query);
            return result;
        }
       
        public DataTable GetTableForExport(long seriesID, double? noDataValue = null, string dateColumn = null, DateTime? firstDate = null, DateTime? lastDate = null)
        {
            var sql =
                "SELECT ds.SeriesID, s.SiteName, v.VariableName, dv.LocalDateTime, dv.DataValue, U1.UnitsName As VarUnits, v.DataType, s.SiteID, s.SiteCode, v.VariableID, v.VariableCode, " +
                "S.Organization, S.SourceDescription, S.SourceLink, v.ValueType, v.TimeSupport, U2.UnitsName As TimeUnits, v.IsRegular, v.NoDataValue, " +
                "dv.UTCOffset, dv.DateTimeUTC, s.Latitude, s.Longitude, dv.ValueAccuracy, dv.CensorCode, m.MethodDescription, q.QualityControlLevelCode, v.SampleMedium, v.GeneralCategory " +
                "FROM DataSeries ds, Sites s, Variables v, DataValues dv, Units U1, Units U2, Methods m, QualityControlLevels q, Sources S " +
                "WHERE v.VariableID = ds.VariableID " +
                "AND s.SiteID = ds.SiteID " +
                "AND m.MethodID = ds.MethodID " +
                "AND q.QualityControlLevelID = ds.QualityControlLevelID " +
                "AND S.SourceID = ds.SourceID " +
                "AND dv.SeriesID = ds.SeriesID " +
                "AND U1.UnitsID = v.VariableUnitsID " +
                "AND U2.UnitsID = v.TimeUnitsID " +
                "AND ds.SeriesID = " + seriesID;
            if (noDataValue.HasValue)
            {
                sql += " AND dv.DataValue != " + noDataValue;
            }

            var cmd = DbOperations.CreateCommand(sql);

            // Append date range filter
            if (!string.IsNullOrEmpty(dateColumn) && 
                firstDate.HasValue && lastDate.HasValue)
            {
                cmd.CommandText += string.Format(" AND ({0} >=  @p1 and {0} <=  @p2)", dateColumn);
                var startDateParameter = DbOperations.AddParameter(cmd, "@p1", DbType.DateTime);
                var endDateParemater = DbOperations.AddParameter(cmd, "@p2", DbType.DateTime);

                startDateParameter.Value = firstDate.Value;
                endDateParemater.Value = lastDate.Value;
            }

            var tbl = DbOperations.LoadTable("values", cmd);

            return tbl;
        }

        #endregion

        public override string TableName
        {
            get { return "DataValues"; }
        }
    }
}