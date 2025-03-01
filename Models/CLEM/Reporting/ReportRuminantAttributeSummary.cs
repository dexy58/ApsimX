﻿using Models.Core;
using Models.CLEM.Activities;
using Models.CLEM.Groupings;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Models.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using Models.CLEM.Interfaces;

namespace Models.CLEM.Reporting
{
    /// <summary>Ruminant Attribute summery reporting</summary>
    /// <summary>This activity summarises the attribute value statistics for groups of individuals</summary>
    [Serializable]
    [ViewName("UserInterface.Views.ReportView")]
    [PresenterName("UserInterface.Presenters.CLEMReportResultsPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [Description("This Report will generate statistics relating to an Attribute value from a specified tag")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Reporting/RuminantAttributeSummary.htm")]
    public class ReportRuminantAttributeSummary : CLEMModel, ICLEMUI, IValidatableObject
    {
        [Link]
        private ResourcesHolder resources = null;
        private RuminantHerd ruminantHerd;

        /// <summary>
        /// Attribute tag to filter by
        /// </summary>
        [Description("Attribute tag")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Attribute tag must be provided")]
        public string AttributeTag { get; set; }

        /// <summary>
        /// Report at initialisation
        /// </summary>
        [Description("Report at start of simulation")]
        [System.ComponentModel.DefaultValue(true)]
        public bool ReportAtStart { get; set; }

        /// <summary>
        /// Report mate values for breeders
        /// </summary>
        [Description("Report values for last mate")]
        [System.ComponentModel.DefaultValue(true)]
        public bool ReportMateValues { get; set; }

        /// <summary>
        /// Report item was generated event handler
        /// </summary>
        public event EventHandler OnReportItemGenerated;

        /// <summary>
        /// The last individual to be added or removed (for reporting)
        /// </summary>
        [JsonIgnore]
        public RuminantAttributeStatisticsEventArgs LastStatistics { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ReportRuminantAttributeSummary()
        {
            SetDefaults();

            Report report = this.FindChild<Report>();
            if (report is null)
            {
                report = new Report() { Parent = this };
                this.Children.Add(report);
            }
        }

        /// <summary>
        /// Report item generated and ready for reporting 
        /// </summary>
        /// <param name="e"></param>
        protected virtual void ReportItemGenerated(RuminantAttributeStatisticsEventArgs e)
        {
            OnReportItemGenerated?.Invoke(this, e);
        }

        /// <summary>
        /// Connect event handlers.
        /// </summary>
        /// <param name="sender">Sender object..</param>
        /// <param name="args">Event data.</param>
        [EventSubscribe("SubscribeToEvents")]
        private void OnConnectToEvents(object sender, EventArgs args)
        {
            Report report = this.FindChild<Report>();
            if (report is null)
            {
                report = new Report();
                this.Children.Add(report);
            }
            report.Name = Name;
            report.VariableNames = new string[] {
                "[Clock].Today as Date",
                $"[{Name}].LastStatistics.GroupName as Group",
                $"[{Name}].LastStatistics.Statistics.Count as CountAttr",
                $"[{Name}].LastStatistics.Statistics.Total as Individuals",
                $"[{Name}].LastStatistics.Statistics.Average as Average",
                $"[{Name}].LastStatistics.Statistics.StandardDeviation as SD",
                $"[{Name}].LastStatistics.Statistics.AverageMate as MateAverage",
                $"[{Name}].LastStatistics.Statistics.StandardDeviationMate as MateSD"
            };
            report.EventNames = new string[] {
                $"[{Name}].OnReportItemGenerated"
            };
            if(!ReportMateValues)
            {
                report.VariableNames = report.VariableNames.Take(6).ToArray();
            }
        }

        /// <summary>An event handler to allow us to initialize ourselves.</summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            ruminantHerd = resources.FindResourceGroup<RuminantHerd>();
            if (ruminantHerd is null) return;
        }

        /// <summary>
        /// Function to report herd individuals each month
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMHerdSummary")]
        private void OnCLEMHerdSummary(object sender, EventArgs e)
        {
            if (TimingOK)
                ReportHerd();
        }

        #region validation
        /// <summary>
        /// Validate model
        /// </summary>
        /// <param name="validationContext"></param>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            ruminantHerd = resources.FindResourceGroup<RuminantHerd>();
            var results = new List<ValidationResult>();
            // check that this activity has a parent of type CropActivityManageProduct

            if (ruminantHerd is null)
            {
                string[] memberNames = new string[] { "Missing resource" };
                results.Add(new ValidationResult($"No ruminant herd resource could be found for [ReportRuminantAttributeSummary] [{this.Name}]", memberNames));
            }
            if (!this.FindAllChildren<RuminantGroup>().Any())
            {
                string[] memberNames = new string[] { "Missing ruminant filter group" };
                results.Add(new ValidationResult($"The [ReportRuminantAttributeSummary] [{this.Name}] requires at least one filter group to identify individuals to report", memberNames));
            }
            return results;
        }

        #endregion

        /// <summary>
        /// Function to report herd individuals each month
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMValidate")]
        private void OncCLEMValidate(object sender, EventArgs e)
        {
            if (ReportAtStart)
                ReportHerd();
        }

        /// <summary>
        /// Do reporting of individuals
        /// </summary>
        /// <returns></returns>
        private void ReportHerd()
        {
            // warning if the same individual is in multiple filter groups it will be considered more than once
            foreach (var fgroup in this.FindAllChildren<RuminantGroup>())
            {
                ListStatistics listStatistics = SummariseAttribute(AttributeTag, true, fgroup);
                if (listStatistics != null)
                {
                    LastStatistics = new RuminantAttributeStatisticsEventArgs()
                    {
                        GroupName = fgroup.Name,
                        Statistics = listStatistics
                    };
                    ReportItemGenerated(LastStatistics); 
                }
            }
        }

        /// <summary>
        /// Return the mean and standard deviation of an attribute value
        /// </summary>
        public ListStatistics SummariseAttribute(string tag, bool ignoreNotFound, RuminantGroup herdGroup = null)
        {
            ListStatistics listStatistics = new ListStatistics();
            if (ruminantHerd is null)
                return listStatistics;

            IEnumerable<Ruminant> herd = null;
            if (herdGroup != null)
                herd = herdGroup.Filter(ruminantHerd.Herd);
            else
                herd = ruminantHerd.Herd;

            var values = herd.Where(a => (ignoreNotFound & a.Attributes.GetValue(tag) == null) ? false : true).Select(a => new Tuple<float, float>(Convert.ToSingle(a.Attributes.GetValue(tag)?.StoredValue), Convert.ToSingle(a.Attributes.GetValue(tag)?.StoredMateValue))).ToList();
            if (values.Count() == 0)
                return listStatistics;

            double sd = 0;
            Single mean = values.Average(a => a.Item1);
            Single sum = values.Sum(d => Convert.ToSingle(Math.Pow(d.Item1 - mean, 2)));
            sd = Math.Sqrt((sum) / values.Count() - 1);
            listStatistics.Average = mean;
            listStatistics.StandardDeviation = sd;

            mean = values.Average(a => a.Item2);
            sum = values.Sum(d => Convert.ToSingle(Math.Pow(d.Item2 - mean, 2)));
            sd = (sum==0)?0:Math.Sqrt((sum) / values.Count() - 1);
            listStatistics.AverageMate = mean;
            listStatistics.StandardDeviationMate = sd;

            listStatistics.Count = values.Count();
            listStatistics.Total = herd.Count();

            return listStatistics;
        }

        /// <summary>
        /// Provides the description of the model settings for summary (GetFullSummary)
        /// </summary>
        /// <param name="formatForParentControl">Use full verbose description</param>
        /// <returns></returns>
        public override string ModelSummary(bool formatForParentControl)
        {
            string html = "";
            return html;
        }
    }

    /// <summary>
    /// New ruminant report item event args
    /// </summary>
    [Serializable]
    public class RuminantAttributeStatisticsEventArgs : EventArgs
    {
        /// <summary>
        /// Name of filter group
        /// </summary>
        public string GroupName { get; set; }
        /// <summary>
        /// The Attribute statistics from the group of individuals
        /// </summary>
        public ListStatistics Statistics { get; set; }
    }

}