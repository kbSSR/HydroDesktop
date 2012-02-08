﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using DotSpatial.Data;
using DotSpatial.Symbology;
using HydroDesktop.Common.Tools;
using HydroDesktop.Configuration;
using HydroDesktop.Database;
using HydroDesktop.Interfaces;
using IProgressHandler = HydroDesktop.Common.IProgressHandler;

namespace HydroDesktop.DataDownload.DataAggregation
{
    /// <summary>
    /// Used for aggregating data values.
    /// </summary>
    internal class Aggregator
    {
        #region Fields

        private readonly AggregationSettings _settings;
        private readonly IFeatureLayer _layer;

        #endregion

        #region Constructors

        /// <summary>
        /// Create new instance of <see cref="Aggregator"/>
        /// </summary>
        /// <param name="settings">Settings</param>
        /// <param name="layer">Layer to update</param>
        /// <exception cref="ArgumentNullException">Raises if <paramref name="settings"/> or <paramref name="layer"/> is null.</exception>
        public Aggregator(AggregationSettings settings, IFeatureLayer layer)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (layer == null) throw new ArgumentNullException("layer");
            Contract.EndContractBlock();

            _settings = settings;
            _layer = layer;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Progress Handler
        /// </summary>
        public IProgressHandler ProgressHandler { get; set; }

        public int MaxPercentage { get; set; }

        #endregion

        #region Public methods

        private static DataColumn FindOrCreateColumn(DataTable dataTable, string columnName, Type columnType)
        {
            var dataColumn = dataTable.Columns.Cast<DataColumn>()
                .FirstOrDefault(column => column.ColumnName == columnName &&
                                          column.DataType == columnType);
            if (dataColumn == null)
            {
                dataColumn = new DataColumn(columnName, columnType);
                dataTable.Columns.Add(dataColumn);
            }
            return dataColumn;
        }

        /// <summary>
        /// Perform aggregation using given settings
        /// </summary>
        /// <returns>Aggregation result</returns>
        public AggregationResult Calculate()
        {
            int percentage = 0;
            ReportProgress(++percentage, "Starting calculation");

            var seriesRepo = RepositoryFactory.Instance.Get<IDataSeriesRepository>(DatabaseTypes.SQLite,
                                                                               Settings.Instance.
                                                                                   DataRepositoryConnectionString);

            IFeatureSet featureSet;
            if (_settings.CreateNewLayer)
            {
                ReportProgress(++percentage, "Creating new layer");

                featureSet = new FeatureSet { Projection = _layer.DataSet.Projection };
                var seriesIDCol = featureSet.DataTable.Columns.Add("SeriesID", typeof(long));
                var siteNameCol = featureSet.DataTable.Columns.Add("SiteName", typeof(string));
                var siteCodeCol = featureSet.DataTable.Columns.Add("SiteCode", typeof(string));

                
                // Find features to add to new feature set
                foreach (var feature in _layer.DataSet.Features)
                {
                    var seriesIDValue = feature.DataRow["SeriesID"];
                    if (seriesIDValue == null || seriesIDValue == DBNull.Value)
                        continue;
                    var seriesID = Convert.ToInt64(seriesIDValue);
                    var series = seriesRepo.GetSeriesByID(seriesID);
                    if (series == null) continue;

                    // Filter by variable code
                    if (series.Variable.Code != _settings.VariableCode)
                    {
                        continue;
                    }
                    
                    var newFeature = featureSet.AddFeature(feature.BasicGeometry);
                    newFeature.DataRow[seriesIDCol] = seriesID;
                    newFeature.DataRow[siteNameCol] = series.Site.Name;
                    newFeature.DataRow[siteCodeCol] = series.Site.Code;
                }
                
                var fileName = Path.Combine(Settings.Instance.CurrentProjectDirectory,
                                            string.Format("{0}-{1}-{2}.shp",
                                                          _settings.AggregationMode,
                                                          _settings.StartTime.ToString("yyyyMMdd"),
                                                          _settings.EndTime.ToString("yyyyMMdd")));
                featureSet.Filename = fileName;
            }
            else
            {
                featureSet = _layer.DataSet;
            }

            // Add column to store data, if it not exists
            ReportProgress(++percentage, "Finding column to store data");
            var columnName = GetColumnName(_settings.AggregationMode);
            var columnType = typeof (double);
            var dataColumn = FindOrCreateColumn(featureSet.DataTable, columnName, columnType);
            var percAvailableColumn = FindOrCreateColumn(featureSet.DataTable, "PercAvailable", columnType);

            // Find series to aggregate
            ReportProgress(++percentage, "Finding series to process");
          
            var idsToProcess = new List<Tuple<IFeature, long>>();
            foreach (var feature in featureSet.Features)
            {
                var seriesIDValue = feature.DataRow["SeriesID"];
                if (seriesIDValue == null || seriesIDValue == DBNull.Value)
                    continue;
                var seriesID = Convert.ToInt64(seriesIDValue);
                var series = seriesRepo.GetSeriesByID(seriesID);
                if (series == null) continue;

                // Filter by variable code
                if (series.Variable.Code != _settings.VariableCode)
                {
                    continue;
                }

                idsToProcess.Add(new Tuple<IFeature, long>(feature, seriesID));
            }

            // Calculating...
            var repo = RepositoryFactory.Instance.Get<IDataValuesRepository>(DatabaseTypes.SQLite,
                                                                             Settings.Instance.DataRepositoryConnectionString);
            var aggregationFunction = GetSQLAggregationFunction(_settings.AggregationMode);
            var minDate = _settings.StartTime;
            var maxDate = _settings.EndTime;
            for (int i = 0; i < idsToProcess.Count; i++)
            {
                var tuple = idsToProcess[i];
                var feature = tuple.Item1;
                var seriesID = tuple.Item2;
                var value = repo.AggregateValues(seriesID, aggregationFunction, minDate, maxDate);
                feature.DataRow[dataColumn] = value;

                // Calculating PercAvailable
                var percAvailabe = repo.CalculatePercAvailable(seriesID, minDate, maxDate);
                feature.DataRow[percAvailableColumn] = percAvailabe;

                // reporting progress
                ReportProgress(percentage + (i + 1) * (MaxPercentage - percentage) / idsToProcess.Count,
                               string.Format("Processed {0}/{1} series", i + 1, idsToProcess.Count));
            }

            var result = new AggregationResult
                             {
                                 FeatureSet = featureSet,
                                 ResultColumnName = dataColumn.ColumnName,
                             };
            return result;
        }

        #endregion

        #region Private methods

        private void ReportProgress(int percentage, object state)
        {
            var progressHandler = ProgressHandler;
            if (progressHandler == null) return;

            progressHandler.ReportProgress(percentage, state);
        }

        private static string GetColumnName(AggregationMode mode)
        {
            return mode.Description();
        }

        private static string GetSQLAggregationFunction(AggregationMode mode)
        {
            switch (mode)
            {
                case AggregationMode.Max:
                    return "Max";
                case AggregationMode.Min:
                    return "Min";
                case AggregationMode.Sum:
                    return "Sum";
                case AggregationMode.Avg:
                    return "Avg";
                default:
                    throw new ArgumentOutOfRangeException("Unknown AggregationMode");
            }
        }

        #endregion
    }

    /// <summary>
    /// Contains results of aggregation
    /// </summary>
    public class AggregationResult
    {
        /// <summary>
        /// Resulted FeatureSet
        /// </summary>
        public IFeatureSet FeatureSet { get; set; }

        /// <summary>
        /// Name of column, which contains aggregation values
        /// </summary>
        public string ResultColumnName { get; set; }
    }
}