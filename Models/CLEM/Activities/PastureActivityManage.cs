﻿using Models.Core;
using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using Models.Core.Attributes;
using System.IO;

namespace Models.CLEM.Activities
{
    /// <summary>Pasture management activity</summary>
    /// <summary>This activity provides a pasture based on land unit, area and pasture type</summary>
    /// <summary>Ruminant move activities place individuals in the paddack after which they will graze pasture for the paddock stored in the PastureP Pools</summary>
    /// <version>1.0</version>
    /// <updates>First implementation of this activity using NABSA grazing processes</updates>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Manages a pasture (GrazeFoodStoreType) by allocating land, tracking pasture state and ecological indicators, communicating with a pasture production database")]
    [Version(1, 0, 2, "Now supports generic pasture production data reader")]
    [Version(1, 0, 2, "Added ecological indicator calculations")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Pasture/ManagePasture.htm")]
    public class PastureActivityManage: CLEMActivityBase, IValidatableObject, IPastureManager
    {
        [Link]
        private Clock clock = null;
        [Link]
        private ZoneCLEM zoneCLEM = null;

        private double unitsOfArea2Ha;
        private IFilePasture filePasture = null;
        private string soilIndex = "0"; // obtained from LandType used
        private double stockingRateSummed;  //summed since last Ecological Calculation.
        private double ha2sqkm = 0.01; //convert ha to square km
        private bool gotLandRequested = false; //was this pasture able to get the land it requested ?
        private List<PastureDataType> pastureDataList;

        /// <summary>
        /// Land type where pasture is located
        /// </summary>
        [Description("Land type where pasture is located")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Land type where pasture is located required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(Land) } })]
        public string LandTypeNameToUse { get; set; }

        /// <summary>
        /// Pasture type to use
        /// </summary>
        [Description("Pasture to manage")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Pasture required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(GrazeFoodStore) } })]
        public string FeedTypeName { get; set; }

        /// <summary>
        /// Name of the model for the pasture input file
        /// </summary>
        [Description("Name of pasture data reader")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Pasture production database reader required")]
        [Models.Core.Display(Type = DisplayType.DropDown, Values = "GetReadersAvailableByName", ValuesArgs = new object[] { new Type[] { typeof(FileCrop), typeof(FileSQLitePasture) } })]
        public string PastureDataReader { get; set; }

        /// <summary>
        /// Starting stocking rate (Adult Equivalents/square km)
        /// </summary>
        [Description("Starting stocking rate (Adult Equivalents/sqkm)")]
        [Required, GreaterThanEqualValue(0)]
        public double StartingStockingRate { get; set; }

        /// <summary>
        /// Area of pasture
        /// </summary>
        [JsonIgnore]
        public double Area { get; set; }

        /// <summary>
        /// Current land condition index
        /// </summary>
        [JsonIgnore]
        public RelationshipRunningValue LandConditionIndex { get; set; }

        /// <summary>
        /// Grass basal area
        /// </summary>
        [JsonIgnore]
        public RelationshipRunningValue GrassBasalArea { get; set; }

        /// <summary>
        /// Area requested
        /// </summary>
        [Description("Area of pasture")]
        [Required, GreaterThanEqualValue(0)]
        public double AreaRequested { get; set; }

        /// <summary>
        /// Use unallocated available
        /// </summary>
        [Description("Use unallocated land")]
        public bool UseAreaAvailable { get; set; }

        /// <summary>
        /// Feed type
        /// </summary>
        [JsonIgnore]
        public GrazeFoodStoreType LinkedNativeFoodType { get; set; }

        /// <summary>
        /// Land item
        /// </summary>
        [JsonIgnore]
        public LandType LinkedLandItem { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public PastureActivityManage()
        {
            TransactionCategory = "Pasture.Manage";
        }

        #region validation
        /// <summary>
        /// Validate this object
        /// </summary>
        /// <param name="validationContext"></param>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            if (LandConditionIndex == null)
            {
                string[] memberNames = new string[] { "RelationshipRunningValue for LandConditionIndex" };
                results.Add(new ValidationResult("Unable to locate the [o=RelationshipRunningValue] for the Land Condition Index [a=Relationship] for this pasture.\r\nAdd a [o=RelationshipRunningValue] named [LC] below a [a=Relationsip] that defines change in land condition with utilisation below this activity", memberNames));
            }
            if (GrassBasalArea == null)
            {
                string[] memberNames = new string[] { "RelationshipRunningValue for GrassBasalArea" };
                results.Add(new ValidationResult("Unable to locate the [o=RelationshipRunningValue] for the Grass Basal Area [a=Relationship] for this pasture.\r\nAdd a [o=RelationshipRunningValue] named [GBA] below a [a=Relationsip] that defines change in grass basal area with utilisation below this activity", memberNames));
            }
            if (filePasture == null)
            {
                string[] memberNames = new string[] { "FilePastureReader" };
                results.Add(new ValidationResult("Unable to locate pasture database file. Add a FilePasture or FileSQLitePasture reader model component to the simulation tree.", memberNames));
            }
            return results;
        }

        #endregion
        
        /// <summary>An event handler to intitalise this activity just once at start of simulation</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseResource")]
        private void OnCLEMInitialiseResource(object sender, EventArgs e)
        {
            // activity is performed in CLEMUpdatePasture not CLEMGetResources and has no labour
            this.AllocationStyle = ResourceAllocationStyle.Manual;

            // locate Land Type resource for this forage.
            LinkedLandItem = Resources.FindResourceType<Land, LandType>(this, LandTypeNameToUse, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
            LandConditionIndex = FindAllDescendants<RelationshipRunningValue>().Where(a => (new string[] { "lc", "landcondition", "landcon", "landconditionindex" }).Contains(a.Name.ToLower())).FirstOrDefault() as RelationshipRunningValue;
            GrassBasalArea = FindAllDescendants<RelationshipRunningValue>().Where(a => (new string[] { "gba", "basalarea", "grassbasalarea" }).Contains(a.Name.ToLower())).FirstOrDefault() as RelationshipRunningValue;
            filePasture = zoneCLEM.Parent.FindAllDescendants().Where(a => a.Name == PastureDataReader).FirstOrDefault() as IFilePasture;

            if (LandConditionIndex is null || GrassBasalArea is null || filePasture is null)
                return;

            LandType land = null;
            if (filePasture != null)
            {
                // check that database has region id and land id
                ZoneCLEM clem = FindAncestor<ZoneCLEM>();
                int recs = filePasture.RecordsFound((filePasture as FileSQLitePasture).RegionColumnName, clem.ClimateRegion);
                if (recs == 0)
                    throw new ApsimXException(this, $"No pasture production records were located by [x={(filePasture as Model).Name}] for [a={this.Name}] given [Region id] = [{clem.ClimateRegion}] as specified in [{clem.Name}]");

                land = Resources.FindResourceType<Land, LandType>(this, LandTypeNameToUse, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
                if (land != null)
                {
                    recs = filePasture.RecordsFound((filePasture as FileSQLitePasture).LandIdColumnName, land.SoilType);
                    if (recs == 0)
                        throw new ApsimXException(this, $"No pasture production records were located by [x={(filePasture as Model).Name}] for [a={this.Name}] given [Land id] = [{land.SoilType}] as specified in [{land.Name}] used to manage the pasture");
                }
            }

            if (UseAreaAvailable)
                LinkedLandItem.TransactionOccurred += LinkedLandItem_TransactionOccurred;

            ResourceRequestList = new List<ResourceRequest>
            {
                new ResourceRequest()
                {
                    Resource = land,
                    AllowTransmutation = false,
                    Required = UseAreaAvailable ? LinkedLandItem.AreaAvailable : AreaRequested,
                    ResourceType = typeof(Land),
                    ResourceTypeName = LandTypeNameToUse.Split('.').Last(),
                    ActivityModel = this,
                    Category = TransactionCategory,
                    FilterDetails = null
                }
            };

            CheckResources(ResourceRequestList, Guid.NewGuid());
            gotLandRequested = TakeResources(ResourceRequestList, false);

            //Now the Land has been allocated we have an Area 
            if (gotLandRequested)
            {            
                //get the units of area for this run from the Land resource parent.
                unitsOfArea2Ha = Resources.FindResourceGroup<Land>().UnitsOfAreaToHaConversion;

                // locate Pasture Type resource
                LinkedNativeFoodType = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, FeedTypeName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);

                //Assign the area actually got after taking it. It might be less than AreaRequested (if partial)
                Area = ResourceRequestList.FirstOrDefault().Provided;

                // ensure no other activity has set the area of this GrazeFoodStore
                LinkedNativeFoodType.Manager = this as IPastureManager;

                soilIndex = ((LandType)ResourceRequestList.FirstOrDefault().Resource).SoilType;

                if (!(LandConditionIndex is null))
                    LinkedNativeFoodType.CurrentEcologicalIndicators.LandConditionIndex = LandConditionIndex.StartingValue;

                if (!(GrassBasalArea is null))
                    LinkedNativeFoodType.CurrentEcologicalIndicators.GrassBasalArea = GrassBasalArea.StartingValue;

                LinkedNativeFoodType.CurrentEcologicalIndicators.StockingRate = StartingStockingRate;
                stockingRateSummed = StartingStockingRate;

                //Now we have a stocking rate and we have starting values for Land Condition and Grass Basal Area
                //get the starting pasture data list from Pasture reader
                if (filePasture != null & LinkedNativeFoodType != null)
                {
                    GetPastureDataList_TodayToNextEcolCalculation();

                    double firstMonthsGrowth = 0;
                    if (pastureDataList != null)
                    {
                        PastureDataType pasturedata = pastureDataList.Where(a => a.Year == clock.StartDate.Year && a.Month == clock.StartDate.Month).FirstOrDefault();
                        firstMonthsGrowth = pasturedata.Growth;
                    }

                    LinkedNativeFoodType.SetupStartingPasturePools(Area * unitsOfArea2Ha, firstMonthsGrowth);
                }
            }
        }

        /// <summary>
        /// Overrides the base class method to allow for clean up
        /// </summary>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            if (LinkedLandItem != null && UseAreaAvailable)
                LinkedLandItem.TransactionOccurred -= LinkedLandItem_TransactionOccurred;
        }

        /// <summary>An event handler to allow us to get next supply of pasture</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMUpdatePasture")]
        private void OnCLEMUpdatePasture(object sender, EventArgs e)
        {
            this.Status = ActivityStatus.Ignored;
            if (pastureDataList != null)
            {
                this.Status = ActivityStatus.NotNeeded;
                double growth = 0;

                //Get this months pasture data from the pasture data list
                PastureDataType pasturedata = pastureDataList.Where(a => a.Year == clock.Today.Year && a.Month == clock.Today.Month).FirstOrDefault();

                growth = pasturedata.Growth;
                //TODO: check units from input files.
                // convert from kg/ha to kg/area unit
                growth *= unitsOfArea2Ha;

                LinkedNativeFoodType.CurrentEcologicalIndicators.Rainfall += pasturedata.Rainfall;
                LinkedNativeFoodType.CurrentEcologicalIndicators.Erosion += pasturedata.SoilLoss;
                LinkedNativeFoodType.CurrentEcologicalIndicators.Runoff += pasturedata.Runoff;
                LinkedNativeFoodType.CurrentEcologicalIndicators.Cover += pasturedata.Cover;
                LinkedNativeFoodType.CurrentEcologicalIndicators.TreeBasalArea += pasturedata.TreeBA;

                if (growth > 0)
                {
                    this.Status = ActivityStatus.Success;
                    GrazeFoodStorePool newPasture = new GrazeFoodStorePool
                    {
                        Age = 0
                    };
                    newPasture.Set(growth * Area);
                    newPasture.Nitrogen = this.LinkedNativeFoodType.GreenNitrogen;
                    newPasture.DMD = newPasture.Nitrogen * LinkedNativeFoodType.NToDMDCoefficient + LinkedNativeFoodType.NToDMDIntercept;
                    newPasture.DMD = Math.Min(100, Math.Max(LinkedNativeFoodType.MinimumDMD, newPasture.DMD));
                    newPasture.Growth = newPasture.Amount;
                    this.LinkedNativeFoodType.Add(newPasture, this, "", "Growth");
                }
            }

            // report activity performed.
            ActivityPerformedEventArgs activitye = new ActivityPerformedEventArgs
            {
                Activity = new BlankActivity()
                {
                    Status = zoneCLEM.IsEcologicalIndicatorsCalculationMonth()? ActivityStatus.Calculation: ActivityStatus.Success,
                    Name = this.Name
                }
            };
            activitye.Activity.SetGuID(this.UniqueID);
            this.OnActivityPerformed(activitye);
        }

        /// <summary>
        /// Function to calculate ecological indicators. 
        /// By summing the monthly stocking rates so when you do yearly ecological calculation 
        /// you can get average monthly stocking rate for the whole year.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMCalculateEcologicalState")]
        private void OnCLEMCalculateEcologicalState(object sender, EventArgs e)
        {
            // This event happens after growth and pasture consumption and animal death
            // But before any management, buying and selling of animals.

            // add this months stocking rate to running total 
            stockingRateSummed += CalculateStockingRateRightNow();

            CalculateEcologicalIndicators();
        }

        private double CalculateStockingRateRightNow()
        {
            var herd = Resources.FindResourceGroup<RuminantHerd>();
            if (herd != null)
            {
                string paddock = FeedTypeName;
                if(paddock.Contains("."))
                    paddock = paddock.Substring(paddock.IndexOf(".")+1);

                return herd.Herd.Where(a => a.Location == paddock).Sum(a => a.AdultEquivalent) / (Area * unitsOfArea2Ha * ha2sqkm);
            }
            else
                return 0;
        }

        /// <summary>
        /// Method to perform calculation of all ecological indicators.
        /// </summary>
        private void CalculateEcologicalIndicators()
        {

            //If it is time to do yearly calculation
            if (zoneCLEM.IsEcologicalIndicatorsCalculationMonth())
            {
                // Calculate change in Land Condition index and Grass basal area
                double utilisation = LinkedNativeFoodType.PercentUtilisation;

                LandConditionIndex.Modify(utilisation);
                LinkedNativeFoodType.CurrentEcologicalIndicators.LandConditionIndex = LandConditionIndex.Value;
                GrassBasalArea.Modify(utilisation);
                LinkedNativeFoodType.CurrentEcologicalIndicators.GrassBasalArea = GrassBasalArea.Value;

                // Calculate average monthly stocking rate
                // Check number of months to use
                int monthdiff = ((zoneCLEM.EcologicalIndicatorsNextDueDate.Year - clock.StartDate.Year) * 12) + zoneCLEM.EcologicalIndicatorsNextDueDate.Month - clock.StartDate.Month+1;
                if (monthdiff >= zoneCLEM.EcologicalIndicatorsCalculationInterval)
                    monthdiff = zoneCLEM.EcologicalIndicatorsCalculationInterval;

                LinkedNativeFoodType.CurrentEcologicalIndicators.StockingRate = stockingRateSummed / monthdiff;

                //perennials
                LinkedNativeFoodType.CurrentEcologicalIndicators.Perennials = 92.2 * (1 - Math.Pow(LandConditionIndex.Value, 3.35) / Math.Pow(LandConditionIndex.Value, 3.35 + 137.7)) - 2.2;

                //%utilisation
                LinkedNativeFoodType.CurrentEcologicalIndicators.Utilisation = utilisation;

                // Reset running total for stocking rate
                stockingRateSummed = 0;

                // calculate averages
                LinkedNativeFoodType.CurrentEcologicalIndicators.Cover /= monthdiff;
                LinkedNativeFoodType.CurrentEcologicalIndicators.TreeBasalArea /= monthdiff;

                //TreeC
                // I didn't include the / area as tba is already per ha. I think NABSA has this wrong
                LinkedNativeFoodType.CurrentEcologicalIndicators.TreeCarbon = LinkedNativeFoodType.CurrentEcologicalIndicators.TreeBasalArea * 6286 * 0.46;

                //methane
                //soilC
                //Burnkg
                //methaneFire
                //N2OFire

                //Get the new Pasture Data using the new Ecological Indicators (ie. GrassBA, LandCon, StRate)
                GetPastureDataList_TodayToNextEcolCalculation();

            }
        }

        /// <summary>
        /// From Pasture File get all the Pasture Data from today to the next Ecological Calculation
        /// </summary>
        private void GetPastureDataList_TodayToNextEcolCalculation()
        {
            // In IAT it only updates the GrassBA, LandCon and StockingRate (Ecological Indicators) 
            // every so many months (specified by  not every month.
            // And the month they are updated on each year is whatever the starting month was for the run.

            // Shaun's code. back to front from NABSA
            //pkGrassBA = (int)(Math.Round(grassBasalArea / 2, 0) * 2); //weird way but this is how NABSA does it.
            //pkLandCon = (int)(Math.Round((landConditionIndex - 1.1) / 2, 0) * 2 + 1);
            //
            // No reason for this grouping so just round.
            //
            // NABSA
            //pkLandCon = (int)(Math.Round(landConditionIndex / 2, 0) * 2); //weird way but this is how NABSA does it.
            //pkGrassBA = (int)(Math.Round((grassBasalArea - 1.1) / 2, 0) * 2 + 1);

            pastureDataList = filePasture.GetIntervalsPastureData(zoneCLEM.ClimateRegion, soilIndex,
               LinkedNativeFoodType.CurrentEcologicalIndicators.GrassBasalArea, LinkedNativeFoodType.CurrentEcologicalIndicators.LandConditionIndex, LinkedNativeFoodType.CurrentEcologicalIndicators.StockingRate, clock.Today.AddDays(1), zoneCLEM.EcologicalIndicatorsCalculationInterval);
        }

        // Method to listen for land use transactions 
        // This allows this activity to dynamically respond when use available area is selected
        private void LinkedLandItem_TransactionOccurred(object sender, EventArgs e)
        {
            Area = LinkedLandItem.AreaAvailable;
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary(bool formatForParentControl)
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(FeedTypeName, "Pasture not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write(" occupies ");
                Land parentLand = null;
                if (LandTypeNameToUse != null && LandTypeNameToUse != "")
                    parentLand = this.FindInScope(LandTypeNameToUse.Split('.')[0]) as Land;

                if (UseAreaAvailable)
                    htmlWriter.Write("the unallocated portion of ");
                else
                {
                    if (parentLand == null)
                        htmlWriter.Write("<span class=\"setvalue\">" + AreaRequested.ToString("#,##0.###") + "</span> <span class=\"errorlink\">[UNITS NOT SET]</span> of ");
                    else
                        htmlWriter.Write("<span class=\"setvalue\">" + AreaRequested.ToString("#,##0.###") + "</span> " + parentLand.UnitsOfArea + " of ");
                }
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(LandTypeNameToUse, "Land not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write("</div>");

                return htmlWriter.ToString(); 
            }
        } 
        #endregion
    }
}
