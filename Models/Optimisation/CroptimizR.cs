﻿using APSIM.Shared.JobRunning;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Core.Run;
using Models.Factorial;
using Models.Interfaces;
using Models.Storage;
using Models.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Models.Sensitivity;
using Models.Core.ApsimFile;

namespace Models.Optimisation
{
    /// <summary>
    /// Enumeration of all suported optimization types.
    /// https://sticsrpacks.github.io/CroptimizR/articles/Available_parameter_estimation_algorithms.html
    /// </summary>
    public enum OptimizationTypeEnum
    {
        /// <summary>
        /// Nelder-Meade simplex method implemented in the nloptr package.
        /// https://sticsrpacks.github.io/CroptimizR/articles/Parameter_estimation_simple_case.html
        /// </summary>
        Simplex,

        /// <summary>
        /// DREAM-zs/Bayesian method implemented in the BayesianTools package.
        /// https://sticsrpacks.github.io/CroptimizR/articles/Parameter_estimation_DREAM.html
        /// </summary>
        Bayesian,
    }

    /// <summary>
    /// Encapsulates CroptimizR: An R package for parameter estimation, uncertainty analysis and sensitivity analysis for Crop Models
    /// </summary>
    /// <remarks>
    /// https://github.com/SticsRPacks/CroptimizR
    /// https://github.com/hol430/ApsimOnR
    /// </remarks>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyAndGridView")]
    [PresenterName("UserInterface.Presenters.PropertyAndTablePresenter")]
    [ValidParent(ParentType = typeof(Simulations))]
    public class CroptimizR : Model, IModelAsTable, IRunnable, IReportsStatus
    {
        /// <summary>
        /// This ID is used to identify temp files used by this tool.
        /// </summary>
        /// <remarks>
        /// Without this, Morri run in paralel could overwrite each other's
        /// temp files, as the temp files would have the same name.
        /// </remarks>
        [JsonIgnore]
        private readonly string id = Guid.NewGuid().ToString();

        /// <summary>
        /// List of parameters
        /// </summary>
        /// <remarks>
        /// Needs to be public so that it gets written to .apsimx file
        /// </remarks>
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        /// <summary>
        /// Name of the predicted data table.
        /// </summary>
        [Description("Predicted table")]
        [Tooltip("Name of the predicted table in the datastore")]
        [Display(Type = DisplayType.TableName)]
        public string PredictedTableName { get; set; }

        /// <summary>
        /// Name of the observed data table.
        /// </summary>
        [Description("Observed table")]
        [Tooltip("Name of the observed table in the datastore")]
        [Display(Type = DisplayType.TableName)]
        public string ObservedTableName { get; set; }

        /// <summary>
        /// Variable to optimise.
        /// </summary>
        [Description("Variables to optimise")]
        [Tooltip("Can select multiple values, separated by commas")]
        //[Display(Type = DisplayType.FieldName)]
        public string[] VariableNames { get; set; }

        /// <summary>
        /// Random seed to be used. Set to null for random results.
        /// </summary>
        [Description("Random seed (optional)")]
        [Tooltip("Optional random seed. Iff set, results will be the same for each execution. Leave empty for randomised results.")]
        public int? RandomSeed { get; set; }

        /// <summary>
        /// Optimization algorithm to be used. Changing this will change <see cref="OptimizationMethod"/>.
        /// </summary>
        /// <remarks>
        /// The reason we need both this enum and the IOptimizationMethod property are because
        /// we want to provide a drop-down in the gui of available optimization methods.
        /// </remarks>
        [Description("Optimization algorithm")]
        [JsonIgnore]
        public OptimizationTypeEnum OptimizationType
        {
            get
            {
                return OptimizationMethod.Type;
            }
            set
            {
                switch (value)
                {
                    case OptimizationTypeEnum.Bayesian:
                        OptimizationMethod = new DreamZs();
                        break;
                    case OptimizationTypeEnum.Simplex:
                        OptimizationMethod = new Simplex();
                        break;
                    default:
                        throw new NotImplementedException($"Unsuported optimization type: {value}");
                }
            }
        }

        /// <summary>
        /// Optimization method to be used.
        /// </summary>
        [Description("Optimization method")]
        [Display(Type = DisplayType.SubModel)]
        public IOptimizationMethod OptimizationMethod { get; set; } = new Simplex();

        /// <summary>
        /// Returns the job's progress as a real number in range [0, 1].
        /// </summary>
        [JsonIgnore]
        public double Progress { get; private set; } = 0;

        /// <summary>
        /// Returns the job's status.
        /// </summary>
        [JsonIgnore]
        public string Status { get; private set; }

        /// <summary>
        /// Gets or sets the table of values.
        /// </summary>
        [JsonIgnore]
        public List<DataTable> Tables
        {
            get
            {
                List<DataTable> tables = new List<DataTable>();

                // Add a parameter table
                DataTable table = new DataTable();
                table.Columns.Add("Name", typeof(string));
                table.Columns.Add("Path", typeof(string));
                table.Columns.Add("LowerBound", typeof(double));
                table.Columns.Add("UpperBound", typeof(double));

                foreach (Parameter param in Parameters)
                {
                    DataRow row = table.NewRow();
                    row["Name"] = param.Name;
                    row["Path"] = param.Path;
                    row["LowerBound"] = param.LowerBound;
                    row["UpperBound"] = param.UpperBound;
                    table.Rows.Add(row);
                }
                tables.Add(table);

                return tables;
            }
            set
            {
                Parameters.Clear();
                foreach (DataRow row in value[0].Rows)
                {
                    Parameter param = new Parameter();
                    if (!Convert.IsDBNull(row["Name"]))
                        param.Name = row["Name"].ToString();
                    if (!Convert.IsDBNull(row["Path"]))
                        param.Path = row["Path"].ToString();
                    if (!Convert.IsDBNull(row["LowerBound"]))
                        param.LowerBound = Convert.ToDouble(row["LowerBound"], CultureInfo.InvariantCulture);
                    if (!Convert.IsDBNull(row["UpperBound"]))
                        param.UpperBound = Convert.ToDouble(row["UpperBound"], CultureInfo.InvariantCulture);
                    if (param.Name != null || param.Path != null)
                        Parameters.Add(param);
                }
            }
        }

        /// <summary>
        /// Invoked whenever the R process writes to stdout.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="e">Event arguments.</param>
        private void OnOutputReceivedFromR(object sender, DataReceivedEventArgs e)
        {
            if (e.Data.Contains("Working:"))
            {
                // Update progress.
                Match match = Regex.Match(e.Data, @"Working: ([^%]+)%");
                string progressString = match.Groups[1].Value;
                if (double.TryParse(progressString, NumberStyles.Float, CultureInfo.CurrentCulture, out double progress))
                    Progress = progress / 100; // The R script reports progress as percent
            }
        }

        /// <summary>
        /// Generates the R script which performs the optimization.
        /// </summary>
        /// <param name="fileName">File path to which the R code will be saved.</param>
        /// <param name="outputPath">Directory/path to which output results will be saved. This is passed as a parameter to croptimizR.</param>
        /// <param name="apsimxFileName">Name of the .apsimx file to be run by the optimisation.</param>
        private void GenerateRScript(string fileName, string outputPath, string apsimxFileName)
        {
            // tbi: package installation. Need to test on a clean VM.
            StringBuilder contents = new StringBuilder();

            contents.AppendLine($"variable_names <- c({string.Join(", ", VariableNames.Select(x => $"'{x.Trim()}'").ToArray())})");

            // If we're reading from the PredictedObserved table, need to fix
            // Predicted./Observed. suffix for the observed variables.
            string[] sanitisedObservedVariables = GetObservedVariableName().Select(x => $"'{x.Trim()}'").ToArray();
            string dateVariable = VariableNames.Any(v => v.StartsWith("Predicted.")) ? "Predicted.Clock.Today" : "Clock.Today";
            contents.AppendLine($"observed_variable_names <- c({string.Join(", ", sanitisedObservedVariables)}, '{dateVariable}')");
            contents.AppendLine($"apsimx_path <- '{typeof(IModel).Assembly.Location.Replace(@"\", @"\\")}'");
            contents.AppendLine($"apsimx_file <- '{apsimxFileName.Replace(@"\", @"\\")}'");
            contents.AppendLine($"simulation_names <- {GetSimulationNames()}");
            contents.AppendLine($"predicted_table_name <- '{PredictedTableName}'");
            contents.AppendLine($"observed_table_name <- '{ObservedTableName}'");
            contents.AppendLine($"param_info <- {GetParamInfo()}");
            contents.AppendLine();
            contents.AppendLine(OptimizationMethod.GenerateOptimizationOptions("optim_options"));
            contents.AppendLine($"optim_options$path_results <- '{outputPath.Replace(@"\", @"\\")}'");
            if (RandomSeed != null)
                contents.AppendLine($"optim_options$ranseed <- {RandomSeed}");
            contents.AppendLine();
            contents.AppendLine($"crit_function <- {OptimizationMethod.CritFunction}");
            contents.AppendLine($"optim_method <- '{OptimizationMethod.ROptimizerName}'");
            contents.AppendLine();
            contents.Append(ReflectionUtilities.GetResourceAsString("Models.Resources.RScripts.OptimizR.r"));

            File.WriteAllText(fileName, contents.ToString());
        }

        private string[] GetObservedVariableName()
        {
            if (PredictedTableName == ObservedTableName)
                return VariableNames.Select(x => x.Replace("Predicted.", "Observed.")).ToArray();
            return VariableNames;
        }

        /// <summary>
        /// Generates an .apsimx file containing replacements model (if it
        /// exists), a datastore, and all children of this model. Saves the
        /// file to disk and returns the absolute path to the file.
        /// </summary>
        private string GenerateApsimXFile()
        {
            Simulations rootNode = FindAncestor<Simulations>();
            string apsimxFileName = GetTempFileName($"apsimx_file_{id}", ".apsimx");

            Simulations sims = new Simulations();
            sims.Children.AddRange(Children.Select(c => Apsim.Clone(c)));
            sims.Children.RemoveAll(c => c is IDataStore);

            IModel replacements = this.FindInScope<Replacements>();
            if (replacements != null && !sims.Children.Any(c => c is Replacements))
                sims.Children.Add(Apsim.Clone(replacements));

            // Search for IDataStore, not DataStore - to allow for StorageViaSockets.
            IDataStore storage = this.FindInScope<IDataStore>();
            IModel newDataStore = new DataStore();
            if (storage != null && storage is IModel m)
                newDataStore.Children.AddRange(m.Children.Select(c => Apsim.Clone(c)));

            sims.Children.Add(newDataStore);
            sims.ParentAllDescendants();

            sims.Write(apsimxFileName);

            string originalFile = rootNode?.FileName;
            if (string.IsNullOrEmpty(originalFile))
                originalFile = storage?.FileName;

            // Copy files across.
            foreach (IReferenceExternalFiles fileReference in (rootNode ?? sims).FindAllDescendants<IReferenceExternalFiles>().Cast<IReferenceExternalFiles>())
            {
                foreach (string file in fileReference.GetReferencedFileNames())
                {
                    string absoluteFileName = PathUtilities.GetAbsolutePath(file, originalFile);
                    string fileName = Path.GetFileName(absoluteFileName);
                    string newPath = Path.GetDirectoryName(sims.FileName);
                    File.Copy(absoluteFileName, Path.Combine(newPath, fileName), true);
                }
            }
            return apsimxFileName;
        }

        /// <summary>
        /// Write a message to the summary table.
        /// This is currently used to record console output generated by R.
        /// </summary>
        /// <param name="message">Message to be written.</param>
        private void WriteMessage(string message)
        {
            IDataStore storage = this.FindInScope<IDataStore>();
            if (storage == null)
                throw new ApsimXException(this, "No datastore is available!");

            string modelPath = this.FullPath;
            string relativeModelPath = modelPath.Replace(this.FullPath + ".", string.Empty);

            DataTable messages = new DataTable("_Messages");
            messages.Columns.Add("SimulationName", typeof(string));
            messages.Columns.Add("ComponentName", typeof(string));
            messages.Columns.Add("Date", typeof(DateTime));
            messages.Columns.Add("Message", typeof(string));
            messages.Columns.Add("MessageType", typeof(int));

            var newRow = messages.NewRow();
            newRow[0] = Name;
            newRow[1] = relativeModelPath;
            newRow[2] = DateTime.Now;
            newRow[3] = message;
            newRow[4] = Convert.ToInt32(Simulation.ErrorLevel.Information);
            messages.Rows.Add(newRow);

            // Messages table will be automatically cleaned, unless the simulations
            // are not run, in which case execution should never reach this point.
            storage.Writer.WriteTable(messages, false);
        }

        /// <summary>
        /// Generate an R named list containing the parameter bounds.
        /// </summary>
        /// <returns></returns>
        private string GetParamInfo()
        {
            string[] lower = Parameters.Select(p => $"'{p.Path}'={p.LowerBound}").ToArray();
            string[] upper = Parameters.Select(p => $"'{p.Path}'={p.UpperBound}").ToArray();
            string lowerBounds = string.Join(", ", lower);
            string upperBounds = string.Join(", ", upper);

            return $"list(lb=c({lowerBounds}), ub=c({upperBounds}))";
        }

        /// <summary>
        /// Return all simulation names generated by all descendant models as a
        /// comma-separated string.
        /// </summary>
        private string GetSimulationNames()
        {
            List<string> simulationNames = new List<string>();
            foreach (ISimulationDescriptionGenerator generator in this.FindAllDescendants<ISimulationDescriptionGenerator>())
                if (!(generator is Simulation sim && sim.Parent is ISimulationDescriptionGenerator))
                    simulationNames.AddRange(generator.GenerateSimulationDescriptions().Select(s => $"'{s.Name}'"));

            string csv = string.Join(", ", simulationNames);
            return $"c({csv})";
        }

        /// <summary>
        /// Returns a unique temporary filename.
        /// </summary>
        /// <param name="name">Base name of the file. The returned filename will contain this name.</param>
        /// <param name="extension">File extension to be used.</param>
        /// <returns>Unique temporary filename.</returns>
        private string GetTempFileName(string name, string extension)
        {
            return Path.ChangeExtension(Path.Combine(Path.GetTempPath(), name + id), extension);
        }

        /// <summary>
        /// Prepare the job for running.
        /// </summary>
        public void Prepare()
        {
            // Do nothing.
        }

        /// <summary>
        /// Run the optimization (and wait for it to finish).
        /// </summary>
        /// <param name="cancelToken">Cancellation token.</param>
        public void Run(CancellationTokenSource cancelToken)
        {
            Progress = 0;

            Status = "Installing R Packages";

            R r = new R(cancelToken.Token);
            r.InstallPackages("remotes", "dplyr", "nloptr", "DiceDesign", "DBI", "cli");
            r.InstallFromGithub("hol430/ApsimOnR", "SticsRPacks/CroptimizR");

            Status = "Generating R Script";
            string fileName = GetTempFileName($"parameter_estimation_{id}", ".r");

            string outputPath = Path.Combine(Path.GetTempPath(), $"croptimizr-output-{id}");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            string apsimxFileName = GenerateApsimXFile();
            GenerateRScript(fileName, outputPath, apsimxFileName);

            // todo - capture stderr as well?
            r.OutputReceived += OnOutputReceivedFromR;

            Status = "Running Parameter Optimization";
            string stdout = r.Run(fileName);
            r.OutputReceived -= OnOutputReceivedFromR;
            WriteMessage(stdout);

            // Copy output files into appropriate output directory, if one is specified. Otherwise, delete them.
            Status = "Reading Output";
            DataTable output = null;
            string apsimxFileDir = FindAncestor<Simulations>()?.FileName;
            if (string.IsNullOrEmpty(apsimxFileDir))
                apsimxFileDir = FindAncestor<Simulation>()?.FileName;
            if (!string.IsNullOrEmpty(apsimxFileDir))
                apsimxFileDir = Path.GetDirectoryName(apsimxFileDir);

            IDataStore storage = FindInScope<IDataStore>();
            bool firstFile = true;
            foreach (string file in Directory.EnumerateFiles(outputPath))
            {
                if (Path.GetExtension(file) == ".Rdata")
                {
                    if (storage != null && storage.Writer != null)
                    {
                        output = ReadRData(file);

                        storage.Writer.WriteTable(output, deleteAllData:firstFile);
                        firstFile = false;
                    }
                }
                if (!string.IsNullOrEmpty(apsimxFileDir))
                    File.Copy(file, Path.Combine(apsimxFileDir, Path.Combine($"{Name}-{Path.GetFileName(file)}")), true);
            }
            Directory.Delete(outputPath, true);

            // Now, we run the simulations with the optimal values, and store
            // the results in a checkpoint called 'After'. Checkpointing has
            // not been implemented on the sockets storage implementation.
            if (FindInScope<IDataStore>().Writer is DataStoreWriter)
            {
                Status = "Running simulations with optimised parameters";
                IEnumerable<CompositeFactor> optimalValues = GetOptimalValues(output);
                RunSimsWithOptimalValues(apsimxFileName, "Optimal", optimalValues);

                // Now run sims without optimal values, to populate the 'Current' checkpoint.
                Status = "Running simulations";
                Runner runner = new Runner(Children);
                List<Exception> errors = runner.Run();
                if (errors != null && errors.Count > 0)
                    throw errors[0];
            }
        }

        /// <summary>
        /// Run all child simulations with the given optimal values,
        /// and store the results in the given checkpoint name.
        /// </summary>
        /// <param name="checkpointName">Name of the checkpoint.</param>
        /// <param name="optimalValues">Changes to be applied to the models.</param>
        /// <param name="fileName">Name of the apsimx file run by the optimiser.</param>
        private void RunSimsWithOptimalValues(string fileName, string checkpointName, IEnumerable<CompositeFactor> optimalValues)
        {
            IDataStore storage = FindInScope<IDataStore>();

            // First, clone the simulations (we don't want to change the values
            // of the parameters in the original file).
            Simulations clonedSims = FileFormat.ReadFromFile<Simulations>(fileName, e => throw e, false);
            
            // Apply the optimal values to the cloned simulations.
            clonedSims = EditFile.ApplyChanges(clonedSims, optimalValues);

            DataStore clonedStorage = clonedSims.FindChild<DataStore>();
            clonedStorage.Close();
            clonedStorage.CustomFileName = storage.FileName;
            clonedStorage.Open();

            // Run the child models of the cloned CroptimizR.
            Runner runner = new Runner(clonedSims);
            List<Exception> errors = runner.Run();
            if (errors != null && errors.Count > 0)
                throw errors[0];
            storage.Writer.AddCheckpoint(checkpointName);
            storage.Writer.SetCheckpointShowGraphs(checkpointName, true);
        }

        /// <summary>
        /// Get the a set of changes (represented by composite factors)
        /// which could be applied to a file, which will apply the optimal
        /// parameter values returned by the optimiser.
        /// </summary>
        /// <param name="data">Datatable.</param>
        private IEnumerable<CompositeFactor> GetOptimalValues(DataTable data)
        {
            DataRow optimal = data.AsEnumerable().FirstOrDefault(r => r["Is Optimal"]?.ToString() == "TRUE");
            foreach (Parameter param in Parameters)
                yield return new CompositeFactor("factor", param.Path, optimal[$"{param.Name} Final"]);
        }

        /// <summary>
        /// Read output data from the .Rdata file generated by CroptimizR.
        /// </summary>
        /// <param name="path">Path to the .Rdata file on disk.</param>
        public DataTable ReadRData(string path)
        {
            StringBuilder script = new StringBuilder();
            script.AppendLine($"load('{path.Replace(@"\", @"\\")}')");
            IEnumerable<string> paramNames = Parameters.Select(p => $"'{p.Name}'");
            script.AppendLine($"param_names <- c({string.Join(", ", paramNames)})");
            script.AppendLine(ReflectionUtilities.GetResourceAsString("Models.Resources.RScripts.read_croptimizr_output.r"));
            R r = new R();
            string scriptPath = GetTempFileName("read_croptimizr_output", ".r");
            File.WriteAllText(scriptPath, script.ToString());
            DataTable table = r.RunToTable(scriptPath);

            // The repetition column will be of type float. Need to change this to int.
            string repCol = "Repetition";
            int[] reps = DataTableUtilities.GetColumnAsIntegers(table, repCol);
            table.Columns.Remove(repCol);
            table.Columns.Add(repCol, typeof(int)).SetOrdinal(0);
            for (int i = 0; i < table.Rows.Count; i++)
                table.Rows[i][0] = reps[i];

            table.TableName = "CroptimizR";
            return table;
        }
    }
}
