﻿using ApsimNG.Interfaces;
using Models;
using Models.Core;
using Models.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserInterface.Interfaces;
using UserInterface.Views;

namespace UserInterface.Presenters
{
    /// <summary>
    /// Presenter for displaying simulation html formatted messages
    /// </summary>
    public class MessagePresenter : IPresenter, IRefreshPresenter
    {
        /// <summary>
        /// The model
        /// </summary>
        private Model model;

        /// <summary>
        /// The view to use
        /// </summary>
        private IMarkdownView genericView;

        /// <summary>
        /// Attach the view
        /// </summary>
        /// <param name="model">The model</param>
        /// <param name="view">The view to attach</param>
        /// <param name="explorerPresenter">The explorer</param>
        public void Attach(object model, object view, ExplorerPresenter explorerPresenter)
        {
            this.model = model as Model;
            this.genericView = view as IMarkdownView;
        }

        public void Refresh()
        {
            this.genericView.Text = CreateMarkdown();
        }

        private string CreateMarkdown()
        {
            int maxErrors = 100;
            using (StringWriter markdownWriter = new StringWriter())
            {
                int terminatedCount = 0;
                // find IStorageReader of simulation
                IModel simulation = model.FindAncestor<Simulation>();
                IDataStore ds = model.FindInScope<IDataStore>() as IDataStore;
                if (ds == null)
                    return markdownWriter.ToString();

                DataTable dataTable = ds.Reader.GetData(simulationNames: new string[] { simulation.Name }, tableName: "_Messages");
                if (dataTable == null)
                {
                    markdownWriter.Write("### Datastore is empty");
                    markdownWriter.Write("  \r\n  \r\nNo simulation has been performed for this farm");
                    return markdownWriter.ToString();
                }
                DataRow[] dataRows = dataTable.Select();
                if (dataRows.Count() > 0)
                {
                    int errorCol = dataRows[0].Table.Columns["MessageType"].Ordinal;
                    int msgCol = dataRows[0].Table.Columns["Message"].Ordinal;
                    dataRows = dataRows.OrderBy(a => a[errorCol].ToString()).ToArray();

                    foreach (DataRow dr in dataRows)
                    {
                        // convert invalid parameter warnings to errors
                        if (dr[msgCol].ToString().StartsWith("Invalid parameter value in model"))
                            dr[errorCol] = "0";
                    }

                    foreach (DataRow dr in dataRows.Take(maxErrors))
                    {
                        bool ignore = false;
                        string msgStr = dr[msgCol].ToString();
                        if (msgStr.Contains("@i:"))
                            ignore = true;

                        if (!ignore)
                        {
                            // trim first two rows of error reporting file and simulation.
                            List<string> parts = new List<string>(msgStr.Split('\n'));
                            if (parts[0].Contains("ERROR in file:"))
                                parts.RemoveAt(0);
                            if (parts[0].Contains("ERRORS in file:"))
                                parts.RemoveAt(0);
                            if (parts[0].Contains("Simulation name:"))
                                parts.RemoveAt(0);
                            msgStr = string.Join("\n", parts.Where(a => a.Trim(' ').StartsWith("at ") == false).ToArray());

                            // remove starter text
                            string[] starters = new string[]
                            {
                            "System.Exception: ",
                            "Models.Core.ApsimXException: "
                            };

                            foreach (string start in starters)
                                if (msgStr.Contains(start))
                                    msgStr = msgStr.Substring(start.Length);

                            string title = "Message";
                            switch (dr[errorCol].ToString())
                            {
                                case "2":
                                    title = "Error";
                                    if (dr[msgCol].ToString().Contains("Invalid parameter value in"))
                                        msgStr = "Invalid parameter values provided";
                                    else
                                    {
                                        msgStr = msgStr.Substring(msgStr.IndexOf(':') + 1);
                                        if (msgStr.Contains("\r\n   --- End of inner"))
                                            msgStr = msgStr.Substring(0, msgStr.IndexOf("\r\n   --- End of inner"));
                                    }
                                    break;
                                case "1":
                                    if (dr[msgCol].ToString().StartsWith("Invalid parameter value in"))
                                    {
                                        title = "Validation error";
                                        msgStr = msgStr.Replace("PARAMETER:", "__Parameter:__");
                                        msgStr = msgStr.Replace("DESCRIPTION:", "__Description:__");
                                        msgStr = msgStr.Replace("PROBLEM:", "__Problem:__");
                                    }
                                    else
                                        title = "Warning";
                                    break;
                                default:
                                    break;
                            }
                            if (msgStr.Contains("terminated normally"))
                            {
                                title = "Success";
                                DataTable dataRows2 = ds.Reader.GetDataUsingSql("Select * FROM _InitialConditions WHERE Name = 'Run on'"); // (simulationName: simulation.Name, tableName: "_InitialConditions");
                                int clockCol = dataRows2.Columns["Value"].Ordinal;  // 8;
                                terminatedCount = Math.Min(terminatedCount, dataRows2.Rows.Count - 1);
                                DateTime lastrun = DateTime.Parse(dataRows2.Rows[terminatedCount][clockCol].ToString());
                                msgStr = "Simulation successfully completed at [" + lastrun.ToShortTimeString() + "] on [" + lastrun.ToShortDateString() + "]";

                                // check for resource shortfall and adjust information accordingly
                                // if table exists
                                if (ds.Reader.TableNames.Contains("ReportResourceShortfalls"))
                                {
                                    // if rows in table
                                    DataTable dataRowsShortfalls = ds.Reader.GetDataUsingSql("Select * FROM ReportResourceShortfalls");
                                    if (dataRowsShortfalls.Rows.Count > 0)
                                    {
                                        title = "Resource shortfalls occurred";
                                        msgStr += "  \r\nA number of resource shortfalls were detected in this simulation. See ReportResourceShortfalls table in DataStore for details.";
                                    }
                                }
                                terminatedCount++;
                            }

                            markdownWriter.Write("  \r\n### ");
                            markdownWriter.Write(title);
                            msgStr = msgStr.Replace("]", "**");
                            msgStr = msgStr.Replace("[r=", @".resource-**");
                            msgStr = msgStr.Replace("[rs=", @".resources-**");
                            msgStr = msgStr.Replace("[a=", @".activity-**");
                            msgStr = msgStr.Replace("[as=", @".activities-**");
                            msgStr = msgStr.Replace("[f=", @".filter-**");
                            msgStr = msgStr.Replace("[g=", @".group-**");
                            msgStr = msgStr.Replace("[t=", @".timer-**");
                            msgStr = msgStr.Replace("[x=", "**");
                            msgStr = msgStr.Replace("[o=", "**");
                            msgStr = msgStr.Replace("[m=", @".market-**");
                            msgStr = msgStr.Replace("[z=", @"clem-**");
                            msgStr = msgStr.Replace("[l=", @".labour-**");
                            msgStr = msgStr.Replace("[", "**");
                            msgStr = msgStr.Replace("\r\n", "  \r\n  \r\n");
                            msgStr = msgStr.Replace("<b>", "**");
                            msgStr = msgStr.Replace("</b>", "**");
                            markdownWriter.Write("  \r\n");
                            markdownWriter.Write(msgStr);
                        }
                    }
                    if (dataRows.Count() > maxErrors)
                    {
                        markdownWriter.Write("## Warning limit reached");
                        markdownWriter.Write("  \r\n  \r\nIn excess of " + maxErrors + " errors and warnings were generated. Only the first " + maxErrors + " are displayes here. PLease refer to the SummaryInformation for the full list of issues.");
                    }
                }
                else
                {
                    markdownWriter.Write("\r\n### Message");
                    markdownWriter.Write("  \r\n  \r\nThis simulation has not been performed");
                }
                return markdownWriter.ToString(); 
            }
        }

        private string CreateHTML()
        {
            // kept in case we want to report messages with full simulation summary in html

            int maxErrors = 100;
            string htmlString = "<!DOCTYPE html>\n" +
                "<html>\n<head>\n<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />\n<style>\n" +
                "body {color: [FontColor]; max-width:1000px; font-size:10pt;}" + 
                ".errorbanner {background-color:red !important; border-radius:5px 5px 0px 0px; color:white; padding:5px; font-weight:bold }" +
                ".errorcontent {background-color:[ContError] !important; margin-bottom:20px; border-radius:0px 0px 5px 5px; border-color:red; border-width:1px; border-style:none solid solid solid; padding:10px;}" +
                ".warningbanner {background-color:orange !important; border-radius:5px 5px 0px 0px; color:white; padding:5px; font-weight:bold }" +
                ".warningcontent {background-color:[ContWarn] !important; margin-bottom:20px; border-radius:0px 0px 5px 5px; border-color:orange; border-width:1px; border-style:none solid solid solid; padding:10px;}" +
                ".messagebanner {background-color:CornflowerBlue !important; border-radius:5px 5px 0px 0px; color:white; padding:5px; font-weight:bold }" +
                ".messagecontent {background-color:[ContMessage] !important; margin-bottom:20px; border-radius:0px 0px 5px 5px; border-color:CornflowerBlue; border-width:1px; border-style:none solid solid solid; padding:10px;}" +
                ".okbanner {background-color:green !important; border-radius:5px 5px 0px 0px; color:white; padding:5px; font-weight:bold }" +
                ".okcontent {background-color:[ContOK] !important; margin-bottom:20px; border-radius:0px 0px 5px 5px; border-color:green; border-width:1px; border-style:none solid solid solid; padding:10px;}" +
                ".holdermain {margin: 20px 0px 20px 0px}" +
                ".resourcelink {color:#996633; font-weight:bold; background-color:Cornsilk !important;border-color:#996633; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px; }" +
                ".activitylink {color:#009999; font-weight:bold; background-color:floralwhite !important;border-color:#009999; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px; }" +
                ".filterlink {border-color:#cc33cc; background-color:#f2e2f2 !important; color:#cc33cc; border-width:1px; border-style:solid; padding: 0px 5px 0px 5px; font-weight:bold; border-radius:3px;}" +
                ".filelink {color:green; font-weight:bold; background-color:mintcream !important;border-color:green; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px; }" +
                ".errorlink {color:white; font-weight:bold; background-color:red !important;border-color:darkred; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px; }" +
                ".setvalue {font-weight:bold; background-color: [ValueSetBack] !important; Color: [ValueSetFont]; border-color:#697c7c; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px;}" +
                ".otherlink {font-weight:bold; color:#333333; background-color:#eeeeee !important;border-color:#999999; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px;}" +
                ".marketlink {font-weight:bold; color:#1785FF; background-color:#DCEEFF !important;border-color:#1785FF; border-width:1px; border-style:solid; padding:0px 5px 0px 5px; border-radius:3px;}" +
                ".messageentry {padding:5px 0px 5px 0px; line-height: 1.7em; }" +
                ".holdermain {margin: 20px 0px 20px 0px}" +
                "@media print { body { -webkit - print - color - adjust: exact; }}" +
                "\n</style>\n</head>\n<body>";

            // apply theme based settings
            if (!Utility.Configuration.Settings.DarkTheme)
            {
                // light theme
                htmlString = htmlString.Replace("[FontColor]", "#000000");

                htmlString = htmlString.Replace("[ContError]", "#FFFAFA");
                htmlString = htmlString.Replace("[ContWarn]", "#FFFFFA");
                htmlString = htmlString.Replace("[ContMessage]", "#FAFAFF");
                htmlString = htmlString.Replace("[ContOK]", "#FAFFFF");
                // values
                htmlString = htmlString.Replace("[ValueSetBack]", "#e8fbfc");
                htmlString = htmlString.Replace("[ValueSetFont]", "#000000");
            }
            else
            {
                // dark theme
                htmlString = htmlString.Replace("[FontColor]", "#E5E5E5");

                htmlString = htmlString.Replace("[ContError]", "#490000");
                htmlString = htmlString.Replace("[ContWarn]", "#A35D00");
                htmlString = htmlString.Replace("[ContMessage]", "#030028");
                htmlString = htmlString.Replace("[ContOK]", "#0C440C");
                // values
                htmlString = htmlString.Replace("[ValueSetBack]", "#49adc4");
                htmlString = htmlString.Replace("[ValueSetFont]", "#0e2023");

            }

            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.WriteLine(htmlString);

                // find IStorageReader of simulation
                IModel simulation = model.FindAncestor<Simulation>();
                IModel simulations = simulation.FindAncestor<Simulations>();
                IDataStore ds = simulations.FindAllChildren<IDataStore>().FirstOrDefault() as IDataStore;
                if (ds == null)
                    return htmlWriter.ToString();

                if (ds.Reader.GetData(simulationNames: new string[] { simulation.Name }, tableName: "_Messages") == null)
                    return htmlWriter.ToString();

                DataRow[] dataRows = ds.Reader.GetData(simulationNames: new string[] { simulation.Name }, tableName: "_Messages").Select();
                if (dataRows.Count() > 0)
                {
                    int errorCol = dataRows[0].Table.Columns["MessageType"].Ordinal;  //7; // 8;
                    int msgCol = dataRows[0].Table.Columns["Message"].Ordinal;  //6; // 7;
                    dataRows = ds.Reader.GetData(simulationNames: new string[] { simulation.Name }, tableName: "_Messages").Select().OrderBy(a => a[errorCol].ToString()).ToArray();

                    foreach (DataRow dr in dataRows)
                        // convert invalid parameter warnings to errors
                        if (dr[msgCol].ToString().StartsWith("Invalid parameter value in model"))
                            dr[errorCol] = "0";

                    foreach (DataRow dr in dataRows.Take(maxErrors))
                    {
                        bool ignore = false;
                        string msgStr = dr[msgCol].ToString();
                        if (msgStr.Contains("@i:"))
                            ignore = true;

                        if (!ignore)
                        {
                            // trim first two rows of error reporting file and simulation.
                            List<string> parts = new List<string>(msgStr.Split('\n'));
                            if (parts[0].Contains("ERROR in file:"))
                                parts.RemoveAt(0);
                            if (parts[0].Contains("ERRORS in file:"))
                                parts.RemoveAt(0);
                            if (parts[0].Contains("Simulation name:"))
                                parts.RemoveAt(0);

                            msgStr = string.Join("\n", parts.Where(a => a.Trim(' ').StartsWith("at ") == false).ToArray());

                            // remove starter text
                            string[] starters = new string[]
                            {
                            "System.Exception: ",
                            "Models.Core.ApsimXException: "
                            };

                            foreach (string start in starters)
                                if (msgStr.Contains(start))
                                    msgStr = msgStr.Substring(start.Length);

                            string title = "Message";
                            string type = "Message";
                            switch (dr[errorCol].ToString())
                            {
                                case "2":
                                    title = "Error";
                                    type = "Error";
                                    if (dr[msgCol].ToString().Contains("Invalid parameter value in"))
                                        msgStr = "Invalid parameter values provided";
                                    else
                                        msgStr = msgStr.Substring(msgStr.IndexOf(':') + 1);
                                    break;
                                case "1":
                                    if (dr[msgCol].ToString().StartsWith("Invalid parameter value in"))
                                    {
                                        title = "Validation error";
                                        type = "Error";
                                        msgStr = msgStr.Replace("PARAMETER:", "__Parameter:__");
                                        msgStr = msgStr.Replace("DESCRIPTION:", "__Description:__");
                                        msgStr = msgStr.Replace("PROBLEM:", "__Problem:__");
                                    }
                                    else
                                    {
                                        title = "Warning";
                                        type = "Warning";
                                    }
                                    break;
                                default:
                                    break;
                            }
                            if (msgStr.Contains("terminated normally"))
                            {
                                type = "Ok";
                                title = "Success";
                                DataTable dataRows2 = ds.Reader.GetDataUsingSql("Select * FROM _InitialConditions WHERE Name = 'Run on'"); // (simulationName: simulation.Name, tableName: "_InitialConditions");
                                int clockCol = dataRows2.Columns["Value"].Ordinal;  // 8;
                                DateTime lastrun = DateTime.Parse(dataRows2.Rows[0][clockCol].ToString());
                                msgStr = "Simulation successfully completed at [" + lastrun.ToShortTimeString() + "] on [" + lastrun.ToShortDateString() + "]";

                                // check for resource shortfall and adjust information accordingly
                                // if table exists
                                if (ds.Reader.TableNames.Contains("ReportResourceShortfalls"))
                                {
                                    // if rows in table
                                    DataTable dataRowsShortfalls = ds.Reader.GetDataUsingSql("Select * FROM ReportResourceShortfalls");
                                    if (dataRowsShortfalls.Rows.Count > 0)
                                    {
                                        type = "warning";
                                        title = "Resource shortfalls occurred";
                                        msgStr += "\nA number of resource shortfalls were detected in this simulation. See ReportResourceShortfalls table in DataStore for details.";
                                    }
                                }
                            }

                            htmlWriter.Write("\n<div class=\"holdermain\">");
                            htmlWriter.Write("\n<div class=\"" + type.ToLower() + "banner\">" + title + "</div>");
                            htmlWriter.Write("\n<div class=\"" + type.ToLower() + "content\">");
                            msgStr = msgStr.Replace("\n", "<br />");
                            msgStr = msgStr.Replace("]", "</span>");
                            msgStr = msgStr.Replace("[r=", "<span class=\"resourcelink\">");
                            msgStr = msgStr.Replace("[rs=", "<span class=\"resourcelink\">");
                            msgStr = msgStr.Replace("[a=", "<span class=\"activitylink\">");
                            msgStr = msgStr.Replace("[as=", "<span class=\"activitylink\">");
                            msgStr = msgStr.Replace("[f=", "<span class=\"filterlink\">");
                            msgStr = msgStr.Replace("[g=", "<span class=\"filterlink\">");
                            msgStr = msgStr.Replace("[t=", "<span class=\"filterlink\">");
                            msgStr = msgStr.Replace("[x=", "<span class=\"filelink\">");
                            msgStr = msgStr.Replace("[o=", "<span class=\"otherlink\">");
                            msgStr = msgStr.Replace("[m=", "<span class=\"marketlink\">");
                            msgStr = msgStr.Replace("[z=", "<span class=\"setvalue\">");
                            msgStr = msgStr.Replace("[l=", "<span class=\"filterlink\">");
                            msgStr = msgStr.Replace("[", "<span class=\"setvalue\">");
                            htmlWriter.Write("\n<div class=\"messageentry\">" + msgStr);
                            htmlWriter.Write("\n</div>");
                            htmlWriter.Write("\n</div>");
                            htmlWriter.Write("\n</div>");
                        }
                    }
                    if (dataRows.Count() > maxErrors)
                    {
                        htmlWriter.Write("\n<div class=\"holdermain\">");
                        htmlWriter.Write("\n <div class=\"warningbanner\">Warning limit reached</div>");
                        htmlWriter.Write("\n <div class=\"warningcontent\">");
                        htmlWriter.Write("\n  <div class=\"activityentry\">In excess of " + maxErrors + " errors and warnings were generated. Only the first " + maxErrors + " are displayes here. PLease refer to the SummaryInformation for the full list of issues.");
                        htmlWriter.Write("\n  </div>");
                        htmlWriter.Write("\n </div>");
                        htmlWriter.Write("\n</div>");
                    }
                }
                else
                {
                    htmlWriter.Write("\n<div class=\"holdermain\">");
                    htmlWriter.Write("\n <div class=\"messagebanner\">Message</div>");
                    htmlWriter.Write("\n <div class=\"messagecontent\">");
                    htmlWriter.Write("\n  <div class=\"activityentry\">This simulation has not been performed");
                    htmlWriter.Write("\n  </div>");
                    htmlWriter.Write("\n </div>");
                    htmlWriter.Write("\n</div>");
                }
                htmlWriter.Write("\n</body>\n</html>");
                return htmlWriter.ToString(); 
            }
        }

        /// <summary>
        /// Detach the view
        /// </summary>
        public void Detach()
        {
        }

    }
}
