﻿using Models.Core;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Models.CLEM.Activities;
using Models.Core.Attributes;
using Models.CLEM.Groupings;
using System.Globalization;

namespace Models.CLEM
{
    /// <summary>Ruminant summary</summary>
    /// <summary>This activity summarizes ruminant herds for reporting</summary>
    /// <summary>Remove if you do not need monthly herd summaries</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("This component will generate summarised details for a herd summary report. It uses the current timing rules and herd filters applied to its branch of the user interface tree. It also requires a suitable report object to be present.")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Reporting/RuminantHerdSummary.htm")]
    public class SummariseRuminantHerd: CLEMModel
    {
        [Link]
        private ResourcesHolder resources = null;
        private int timestep = 0;
        private RuminantHerd ruminantHerd;

        /// <summary>
        /// Tracks the active selection in the value box
        /// </summary>
        [Description("Grouping style")]
        public SummarizeRuminantHerdStyle GroupStyle { get; set; }

        /// <summary>
        /// Report item was generated event handler
        /// </summary>
        public event EventHandler OnReportItemGenerated;

        /// <summary>
        /// The details of the summary group for reporting
        /// </summary>
        [JsonIgnore]
        public HerdReportItemGeneratedEventArgs ReportDetails { get; set; }

        /// <summary>
        /// List of filters that define the herd
        /// </summary>
        [JsonIgnore]
        private List<RuminantGroup> herdFilters { get; set; }

        /// <summary>
        /// Report item generated and ready for reporting 
        /// </summary>
        /// <param name="e"></param>
        protected virtual void ReportItemGenerated(HerdReportItemGeneratedEventArgs e)
        {
            OnReportItemGenerated?.Invoke(this, e);
        }

        /// <summary>
        /// List of filters that define the herd
        /// </summary>
        public List<RuminantGroup> HerdFilters { get; set; }

        /// <summary>An event handler to allow us to initialize ourselves.</summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            ruminantHerd = resources.FindResourceGroup<RuminantHerd>();

            // determine any herd filtering
            herdFilters = new List<RuminantGroup>();
            IModel current = this;
            while (current.GetType() != typeof(ZoneCLEM))
            {
                var filtergroup = current.Children.OfType<RuminantGroup>();
                if (filtergroup.Count() > 1)
                {
                    Summary.WriteWarning(this, "Multiple ruminant filter groups have been supplied for [" + current.Name + "]" + Environment.NewLine + "Only the first filter group will be used.");
                }
                if (filtergroup.FirstOrDefault() != null)
                {
                    herdFilters.Insert(0, filtergroup.FirstOrDefault());
                }
                current = current.Parent as IModel;
            }

            // get full name for reporting
            current = this;
            string name = this.Name;
            while (current.GetType() != typeof(ZoneCLEM))
            {
                string quoteName = (current.GetType() == typeof(ActivitiesHolder)) ? "["+current.Name+"]":current.Name;
                name = quoteName + "." + name;
                current = current.Parent as IModel;
            }
        }

        /// <summary>
        /// Function to summarise the herd based on cohorts each month
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMHerdSummary")]
        private void OnCLEMHerdSummary(object sender, EventArgs e)
        {
            timestep++;

            if (!this.TimingOK) return;

            IEnumerable<Ruminant> herd = ruminantHerd?.Herd;
            foreach (RuminantGroup group in herdFilters)
                herd = group.Filter(herd);

            IEnumerable<IGrouping<Tuple<string, string, Sex, string>, Ruminant>> groups = null;

            if (GroupStyle != SummarizeRuminantHerdStyle.Classic)
            {
                switch (GroupStyle)
                {
                    case SummarizeRuminantHerdStyle.ByClass:
                        groups = herd.GroupBy(a => new Tuple<string, string, Sex, string>(a.Breed, a.HerdName, a.Sex, a.Class)) as IEnumerable<IGrouping<Tuple<string, string, Sex, string>, Ruminant>>;
                        break;
                    case SummarizeRuminantHerdStyle.ByAgeYears:
                        groups = herd.GroupBy(a => new Tuple<string, string, Sex, string>(a.Breed, a.HerdName, a.Sex, Math.Truncate(a.Age / 12.0).ToString())) as IEnumerable<IGrouping<Tuple<string, string, Sex, string>, Ruminant>>;
                        break;
                    case SummarizeRuminantHerdStyle.ByAgeMonths:
                        groups = herd.GroupBy(a => new Tuple<string, string, Sex, string>(a.Breed, a.HerdName, a.Sex, a.Age.ToString())) as IEnumerable<IGrouping<Tuple<string, string, Sex, string>, Ruminant>>;
                        break;
                    default:
                        break;
                }

                // decide what groups to use
                groups = herd.GroupBy(a => new Tuple<string, string, Sex, string>(a.Breed, a.HerdName, a.Sex, a.Class)) as IEnumerable<IGrouping<Tuple<string, string, Sex, string>, Ruminant>>;

                var result = groups.Select(group => new
                {
                    Group = group.Key,
                    Info = new HerdReportItemGeneratedEventArgs
                    {
                        TimeStep = timestep,
                        Breed = group.Key.Item1,
                        Herd = group.Key.Item2,
                        Sex = group.Key.Item3.ToString(),
                        Group = group.Key.Item4,
                        Number = group.Count(),
                        Age = group.Average(a => a.Age),
                        AverageWeight = group.Average(a => a.Weight),
                        AverageProportionOfHighWeight = group.Average(a => a.ProportionOfHighWeight),
                        AverageProportionOfNormalisedWeight = group.Average(a => a.ProportionOfNormalisedWeight),
                        AverageIntake = group.Average(a => a.ProportionOfPotentialIntakeObtained),
                        AverageProportionPotentialIntake = group.Average(a => a.ProportionOfPotentialIntakeObtained),
                        AverageWeightGain = group.Average(a => a.WeightGain),
                        AdultEquivalents = group.Sum(a => a.AdultEquivalent),
                        NumberPregnant = (group.Key.Item3 == Sex.Female) ? group.OfType<RuminantFemale>().Where(a => a.IsPregnant).Count() : 0,
                        NumberLactating = (group.Key.Item3 == Sex.Female) ? group.OfType<RuminantFemale>().Where(a => a.IsLactating).Count() : 0,
                        NumberOfBirths = (group.Key.Item3 == Sex.Female) ? group.OfType<RuminantFemale>().Sum(a => a.NumberOfBirthsThisTimestep) : 0,
                    }
                });

                foreach (var item in result)
                {
                    ReportDetails = item.Info;
                    ReportItemGenerated(ReportDetails);
                }
            }
            else
            {
                // old classic approach

                // group by breed
                foreach (var breedGroup in herd.GroupBy(a => a.Breed))
                {
                    // group by herd
                    foreach (var herdGroup in breedGroup.GroupBy(a => a.HerdName))
                    {
                        // group by sex
                        foreach (var sexGroup in herdGroup.GroupBy(a => a.Sex))
                        {
                            // weaned
                            foreach (var ageGroup in sexGroup.OrderBy(a => a.Age).GroupBy(a => Math.Truncate(a.Age / 12.0)))
                            {
                                ReportDetails = new HerdReportItemGeneratedEventArgs
                                {
                                    TimeStep = timestep,
                                    Breed = breedGroup.Key,
                                    Herd = herdGroup.Key,
                                    Age = Convert.ToInt32(ageGroup.Key, CultureInfo.InvariantCulture),
                                    Sex = sexGroup.Key.ToString(), //.Substring(0, 1),
                                    Number = ageGroup.Sum(a => a.Number),
                                    AverageWeight = ageGroup.Average(a => a.Weight),
                                    AverageWeightGain = ageGroup.Average(a => a.WeightGain),
                                    AverageIntake = ageGroup.Average(a => (a.Intake + a.MilkIntake)), //now daily/30.4;
                                    AdultEquivalents = ageGroup.Sum(a => a.AdultEquivalent)
                                };
                                if (sexGroup.Key == Sex.Female)
                                {
                                    ReportDetails.NumberPregnant = ageGroup.Cast<RuminantFemale>().Where(a => a.IsPregnant).Count();
                                    ReportDetails.NumberLactating = ageGroup.Cast<RuminantFemale>().Where(a => a.IsLactating).Count();
                                    ReportDetails.NumberOfBirths = ageGroup.Cast<RuminantFemale>().Sum(a => a.NumberOfBirthsThisTimestep);
                                }
                                else
                                {
                                    ReportDetails.NumberPregnant = 0;
                                    ReportDetails.NumberLactating = 0;
                                    ReportDetails.NumberOfBirths = 0;
                                }

                                ReportItemGenerated(ReportDetails);

                                // reset birth count
                                if (sexGroup.Key == Sex.Female)
                                    ageGroup.Cast<RuminantFemale>().ToList().ForEach(a => a.NumberOfBirthsThisTimestep = 0);
                            }
                        }
                    }
                }
            }
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
    /// Style for reporting groups in Summarize ruminant herd
    /// </summary>
    public enum SummarizeRuminantHerdStyle
    {
        /// <summary>
        /// Use original method with age in years
        /// </summary>
        Classic,
        /// <summary>
        /// Group by class
        /// </summary>
        ByClass,
        /// <summary>
        /// Group by age in years
        /// </summary>
        ByAgeYears,
        /// <summary>
        /// Group by age in months 
        /// </summary>
        ByAgeMonths,
    }


    /// <summary>
    /// New herd report item generated event args
    /// </summary>
    [Serializable]
    public class HerdReportItemGeneratedEventArgs : EventArgs
    {
        /// <summary>
        /// Timestep
        /// </summary>
        public int TimeStep { get; set; }
        /// <summary>
        /// Breed of individuals
        /// </summary>
        public string Breed { get; set; }
        /// <summary>
        /// Herd of individuals
        /// </summary>
        public string Herd { get; set; }
        /// <summary>
        /// Provides value of age or class specified
        /// </summary>
        public string Group { get; set; }
        /// <summary>
        /// Age of individuals (lower bound of year class)
        /// </summary>
        public double Age { get; set; }
        /// <summary>
        /// Sex of individuals
        /// </summary>
        public string Sex { get; set; }
        /// <summary>
        /// Number of individuals
        /// </summary>
        public double Number { get; set; }
        /// <summary>
        /// Average weight of individuals
        /// </summary>
        public double AverageWeight { get; set; }
        /// <summary>
        /// Average weight gain of individuals
        /// </summary>
        public double AverageWeightGain { get; set; }
        /// <summary>
        /// Average proportion of weight to height weight
        /// </summary>
        public double AverageProportionOfHighWeight { get; set; }
        /// <summary>
        /// Average proportion of weight to normalised weight
        /// </summary>
        public double AverageProportionOfNormalisedWeight { get; set; }
        /// <summary>
        /// Average intake of individuals
        /// </summary>
        public double AverageIntake { get; set; }
        /// <summary>
        /// Average proportion intake of potential intake
        /// </summary>
        public double AverageProportionPotentialIntake { get; set; }
        /// <summary>
        /// Adult equivalent of individuals
        /// </summary>
        public double AdultEquivalents { get; set; }
        /// <summary>
        /// Births of individual
        /// </summary>
        public int NumberOfBirths { get; set; }
        /// <summary>
        /// Number pregnant
        /// </summary>
        public int NumberPregnant { get; set; }
        /// <summary>
        /// Number lactating
        /// </summary>
        public int NumberLactating { get; set; }
    }

}
