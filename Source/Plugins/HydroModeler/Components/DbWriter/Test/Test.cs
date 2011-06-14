﻿using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using CUAHSI.HIS;
using OpenMI.Standard;
using Oatc.OpenMI.Sdk.Backbone;
using Oatc.OpenMI.Gui.Core;
using Oatc.OpenMI.Sdk.DevelopmentSupport;
using Oatc.OpenMI.Sdk.Buffer;
using System.Collections;
using System.Windows.Forms;

namespace CUAHSI.HIS.Test
{
    [TestFixture]
    public class HISOpenMIComponentTests
    {
        [Test]
        public void GetComponentDescription()
        {
            Console.Write("Begin Get Component Description Test...");
            DbWriter his = new DbWriter();
            IArgument[] arguments = new IArgument[1];
            arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
            his.Initialize(arguments);

            Console.WriteLine("component description: " + his.ComponentDescription);
        }

        [Test]
        public void GetModelDescription()
        {
            Console.Write("Begin Get Model Description Test...");
            DbWriter his = new DbWriter();
            IArgument[] arguments = new IArgument[1];
            arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
            his.Initialize(arguments);

            Console.WriteLine("model description: " + his.ModelDescription);
        }

        /// <summary>
        /// This method tests hydrolinks ability get values
        /// </summary>
        [Test]
        public void GetValues()
        {
            //Console.Write("Begin Get Values Test...");
            ////create the his component
            //DbWriter dbwriter = new DbWriter();
            //IArgument[] arguments = new IArgument[2];
            //arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
            //arguments[1] = new Argument("Relaxation", "1", true, "Time interpolation factor, btwn 0(linear inter.) and 1(nearest neigbor)");
            //dbwriter.Initialize(arguments);

            //RandomInputGenerator.InputGenerator rand = new InputGenerator();
            //IArgument[] arg = new IArgument[1];
            //arg[0] = new Argument("ElementCount", "1", true, "");
            //rand.Initialize(arg);

            //////create dbreader component
            ////DbReader dbreader = new DbReader();
            ////IArgument[] arguments2 = new IArgument[2];
            ////arguments2[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
            ////arguments2[1] = new Argument("Relaxation", "1", true, "Time interpolation factor, btwn 0(linear inter.) and 1(nearest neigbor)");
            ////dbreader.Initialize(arguments2);

            ////create a trigger component
            //Trigger trigger = new Trigger();
            //trigger.Initialize(null);

            ////link the components
            //Link link = new Link();
            //link.ID = "link-1";
            //link.TargetElementSet = dbwriter.GetInputExchangeItem(0).ElementSet;
            //link.TargetQuantity = dbwriter.GetInputExchangeItem(0).Quantity;
            //link.TargetComponent = dbwriter;
            //link.SourceElementSet = rand.GetOutputExchangeItem(0).ElementSet;
            //link.SourceQuantity = rand.GetOutputExchangeItem(0).Quantity;
            //link.SourceComponent = rand;
            //dbwriter.AddLink(link);
            //Link link2 = new Link();
            //link2.ID = "link-2";
            //link2.TargetElementSet = trigger.GetInputExchangeItem(0).ElementSet;
            //link2.TargetQuantity = trigger.GetInputExchangeItem(0).Quantity;
            //link2.TargetComponent = trigger;
            //link2.SourceElementSet = rand.GetOutputExchangeItem(0).ElementSet;
            //link2.SourceQuantity = rand.GetOutputExchangeItem(0).Quantity;
            //link2.SourceComponent = dbwriter;
            //dbwriter.AddLink(link2);

            ////prepare 
            //rand.Prepare();
            //dbwriter.Prepare();

            //DateTime dt = Convert.ToDateTime("2009-08-20");

            //while (dt <= Convert.ToDateTime("2009-08-21"))
            //{
            //    Application.DoEvents();
            //    ITimeStamp time_stmp = new TimeStamp(CalendarConverter.Gregorian2ModifiedJulian(dt));
            //    ScalarSet scalarset = (ScalarSet)trigger.GetValues(time_stmp, "link-2");
            //    Console.WriteLine("GetValues: " + dt.ToString("s"));
            //    int i = 0;
            //    foreach (double d in scalarset.data)
            //    {
            //        Console.WriteLine(link.SourceElementSet.GetElementID(i).ToString() + " " + d.ToString());
            //        ++i;
            //    }
            //    dt = dt.AddMinutes(5);
            //}
            //dbwriter.Finish();
            //Console.Write("done. \n");
        }

        /// <summary>
        /// This method tests hydrolinks ability to construct a time horizon by reading the waterml database
        /// </summary>
        [Test]
        public void GetTimeHorizon()
        {
            Console.WriteLine("Begin Get Time Horizon Test...");
            //create the his component
            DbWriter his = new DbWriter();
            IArgument[] arguments = new IArgument[1];
            arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
            his.Initialize(arguments);

            //get earliest and latest times
            Console.WriteLine("start: " + CalendarConverter.ModifiedJulian2Gregorian(his.TimeHorizon.Start.ModifiedJulianDay).ToString());
            Console.WriteLine("end: " + CalendarConverter.ModifiedJulian2Gregorian(his.TimeHorizon.End.ModifiedJulianDay).ToString());
        }

        ///// <summary>
        ///// This method tests Hydrolinks ability to read elements from the waterml database
        ///// </summary>
        //[Test]
        //public void GetElements()
        //{
        //    Console.Write("Begin Get Elemtents Test...");
        //    DbReader his = new DbReader();
        //    IArgument[] arguments = new IArgument[1];
        //    arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
        //    his.Initialize(arguments);
        //    for (int i = 0; i < his.OutputExchangeItemCount; ++i)
        //    {
        //        IElementSet es = his.GetOutputExchangeItem(i).ElementSet;
        //        for (int j = 0; j < es.ElementCount; ++j)
        //        {
        //            Console.WriteLine(es.GetXCoordinate(j, 0).ToString() + ", " +
        //                es.GetYCoordinate(j, 0).ToString());
        //        }
        //    }
        //    Console.WriteLine("model description: " + his.ModelDescription);

        //}



        ///// <summary>
        ///// This method tests the implementation of the linear interpolation algorithm (part of the smart
        ///// buffer) within Hydrolink.  Values are added to the smart buffer in 10min intervals, then are 
        ///// requested at 5min intervals.
        ///// </summary>
        //[Test]
        //public void LinearTimeInterpolation()
        //{
        //    Console.Write("Begin Linear Interpolation Test...");

        //    //create the his component
        //    DbReader his = new DbReader();
        //    IArgument[] arguments = new IArgument[1];
        //    arguments[0] = new Argument("DbPath", @"..\data\cuahsi-his\demo.db", true, "Database");
        //    his.Initialize(arguments);

        //    //create a trigger component
        //    Trigger trigger = new Trigger();
        //    trigger.Initialize(null);

        //    //link the two components
        //    Link link = new Link();
        //    link.ID = "link-1";
        //    link.TargetElementSet = trigger.GetInputExchangeItem(0).ElementSet;
        //    link.TargetQuantity = trigger.GetInputExchangeItem(0).Quantity;
        //    link.TargetComponent = trigger;


        //    link.SourceElementSet = his.GetOutputExchangeItem(1).ElementSet;
        //    link.SourceQuantity = his.GetOutputExchangeItem(1).Quantity;
        //    link.TargetComponent = his;


        //    //Spatial interpolation
        //    IDataOperation dataOp = (his).GetOutputExchangeItem(0).GetDataOperation(7);
        //    link.AddDataOperation(dataOp);


        //    //run configuration
        //    his.AddLink(link);

        //    trigger.Validate();
        //    his.Validate();

        //    //prepare 
        //    his.Prepare();

        //    DateTime dt = Convert.ToDateTime("2009-08-20T21:40:00");

        //    SmartBuffer _smartBuffer = new SmartBuffer();

        //    //Add all values to buffer in 10min intervals
        //    Console.Write("Storing values in the smart buffer (10min resolution)... ");
        //    while (dt <= Convert.ToDateTime("2009-08-21T02:00:00"))
        //    {

        //        ITimeStamp time_stmp = new TimeStamp(CalendarConverter.Gregorian2ModifiedJulian(dt));
        //        ScalarSet scalarset = (ScalarSet)his.GetValues(time_stmp, "link-1");

        //        if (scalarset.Count == 0)
        //        {
        //            int f = his.GetOutputExchangeItem(1).ElementSet.ElementCount;
        //            ArrayList zeroArray = new ArrayList();
        //            for (int i = 0; i <= f - 1; i++)
        //                zeroArray.Add(0.0);

        //            double[] zeros = (double[])zeroArray.ToArray(typeof(double));

        //            scalarset = new ScalarSet(zeros);
        //        }

        //        _smartBuffer.AddValues(time_stmp, scalarset);

        //        dt = dt.AddMinutes(10);
        //    }
        //    Console.WriteLine("done.\n\n");

        //    //request values from the smart buffer at 5min intervals
        //    dt = Convert.ToDateTime("2009-08-20T21:40:00");
        //    while (dt <= Convert.ToDateTime("2009-08-21T02:00:00"))
        //    {

        //        ITimeStamp time_stmp = new TimeStamp(CalendarConverter.Gregorian2ModifiedJulian(dt));

        //        //Get values at requested time
        //        ScalarSet scalarset = (ScalarSet)_smartBuffer.GetValues(time_stmp);

        //        Console.WriteLine("GetValues: " + dt.ToString("s"));


        //        //loop through interpolated values
        //        int i = 0;
        //        foreach (double d in scalarset.data)
        //        {

        //            Console.WriteLine(link.SourceElementSet.GetElementID(i).ToString() + " " + d.ToString());
        //            ++i;
        //        }
        //        dt = dt.AddMinutes(5);
        //    }

        //    Console.Write("done. \n");
        //}
    }
}