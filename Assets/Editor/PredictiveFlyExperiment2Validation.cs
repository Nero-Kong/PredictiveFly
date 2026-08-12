#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PredictiveFlyExperiment2Validation
{
    const string OfficialScenePath = "Assets/Scenes/IrairaBou3D_Official.unity";

    [MenuItem("PredictiveFly/Experiment 2/Install In Official Scene")]
    public static void InstallInOfficialScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play Mode before installing Experiment 2.");
        }

        AssetDatabase.ImportAsset(
            OfficialScenePath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != OfficialScenePath)
        {
            scene = EditorSceneManager.OpenScene(OfficialScenePath, OpenSceneMode.Single);
        }

        IrairaBou3DOfficialSceneBuilder builder = FindOfficialSceneBuilder(scene);
        if (builder == null)
        {
            throw new InvalidOperationException(BuildMissingBuilderMessage(scene));
        }

        PredictiveFlyObjectiveLogger logger = builder.GetComponent<PredictiveFlyObjectiveLogger>();
        if (logger == null)
        {
            logger = builder.gameObject.AddComponent<PredictiveFlyObjectiveLogger>();
        }

        PredictiveFlyExperiment2Controller experiment2 =
            builder.GetComponent<PredictiveFlyExperiment2Controller>();
        if (experiment2 == null)
        {
            experiment2 = builder.gameObject.AddComponent<PredictiveFlyExperiment2Controller>();
        }

        PredictiveFlyExperimentController experiment1 =
            builder.GetComponent<PredictiveFlyExperimentController>();
        PredictiveGhostAvatarLocomotion locomotion =
            UnityEngine.Object.FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();

        builder.experimentProtocol = IrairaBou3DOfficialSceneBuilder.ExperimentProtocol.Experiment2;
        experiment2.sceneBuilder = builder;
        experiment2.logger = logger;
        experiment2.locomotion = locomotion;
        experiment2.enabled = true;
        if (experiment1 != null)
        {
            experiment1.enabled = false;
        }

        logger.useKeyboardControls = false;
        logger.enabled = true;
        logger.stopOnFinish = true;
        logger.stopOnModeChange = true;
        logger.saveIncompleteTrials = true;
        logger.incompleteTrialSubdirectory = "Incomplete";
        logger.outputDirectory = experiment2.objectiveOutputDirectory;

        EditorUtility.SetDirty(builder);
        EditorUtility.SetDirty(experiment2);
        EditorUtility.SetDirty(logger);
        if (experiment1 != null)
        {
            EditorUtility.SetDirty(experiment1);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Official scene could not be saved.");
        }

        Debug.Log("[PredictiveFly] Experiment 2 controller installed in the official scene.");
    }

    [MenuItem("PredictiveFly/Experiment 2/Validate Schedule And Scene")]
    public static void ValidateScheduleAndScene()
    {
        ValidateSchedule();
        ValidateOfficialScene();
        Debug.Log("[PredictiveFly] Experiment 2 validation passed.");
    }

    public static void InstallAndValidateBatch()
    {
        try
        {
            ValidateSchedule();
            InstallInOfficialScene();
            ValidateOfficialScene();
            AssetDatabase.SaveAssets();
            Debug.Log("EXPERIMENT2_VALIDATION_PASSED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("EXPERIMENT2_VALIDATION_FAILED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            throw;
        }
    }

    public static void ValidateLogicBatch()
    {
        try
        {
            ValidateSchedule();
            ValidateSyntheticConfiguration();
            Debug.Log("EXPERIMENT2_LOGIC_VALIDATION_PASSED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("EXPERIMENT2_LOGIC_VALIDATION_FAILED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            throw;
        }
    }

    static void ValidateSchedule()
    {
        int first500Count = 0;
        int first1000Count = 0;
        int[,] conditionRouteCounts = new int[6, 4];
        int[,] calibrationAnchorCounts = new int[2, 2];
        Dictionary<string, int>[] blockOrderCounts =
        {
            new Dictionary<string, int>(),
            new Dictionary<string, int>()
        };
        Dictionary<string, int>[] delayOrderCounts =
        {
            new Dictionary<string, int>(),
            new Dictionary<string, int>()
        };

        for (int participant = 1; participant <= 12; participant++)
        {
            int firstDelay = PredictiveFlyExperiment2Controller.GetFirstDelayMilliseconds(participant);
            if (firstDelay == 500)
            {
                first500Count++;
            }
            else if (firstDelay == 1000)
            {
                first1000Count++;
            }
            else
            {
                Fail($"Participant {participant} has invalid first delay {firstDelay}.");
            }

            HashSet<int> participantConditions = new HashSet<int>();
            for (int block = 0; block < 2; block++)
            {
                HashSet<PredictiveFlyExperiment2Controller.ProxyStrategy> strategies =
                    new HashSet<PredictiveFlyExperiment2Controller.ProxyStrategy>();
                string order = string.Empty;
                int blockDelay = -1;
                for (int position = 0; position < 3; position++)
                {
                    int trial = block * 3 + position + 1;
                    if (!PredictiveFlyExperiment2Controller.TryGetTrialAssignment(
                            participant,
                            trial,
                            out PredictiveFlyExperiment2Controller.TrialAssignment assignment))
                    {
                        Fail($"Participant {participant}, trial {trial} did not resolve.");
                    }

                    blockDelay = blockDelay < 0 ? assignment.delayMilliseconds : blockDelay;
                    if (assignment.delayMilliseconds != blockDelay)
                    {
                        Fail($"Participant {participant}, block {block + 1} mixes delay levels.");
                    }
                    strategies.Add(assignment.strategy);
                    participantConditions.Add(assignment.canonicalConditionCode);
                    conditionRouteCounts[assignment.canonicalConditionCode, (int)assignment.route]++;
                    order += ((int)assignment.strategy).ToString();
                }

                if (strategies.Count != 3)
                {
                    Fail($"Participant {participant}, block {block + 1} does not contain all three strategies.");
                }
                blockOrderCounts[block][order] = blockOrderCounts[block].TryGetValue(order, out int count)
                    ? count + 1
                    : 1;
                int strategyDelayIndex = blockDelay == 500 ? 0 : 1;
                delayOrderCounts[strategyDelayIndex][order] =
                    delayOrderCounts[strategyDelayIndex].TryGetValue(order, out int delayOrderCount)
                        ? delayOrderCount + 1
                        : 1;

                int delayOrderPosition = block;
                int delay = PredictiveFlyExperiment2Controller.GetDelayForOrderPosition(
                    participant,
                    delayOrderPosition);
                string firstAnchor = PredictiveFlyExperiment2Controller.GetCalibrationAnchor(
                    participant,
                    delayOrderPosition,
                    0);
                int delayIndex = delay == 500 ? 0 : 1;
                int anchorIndex = firstAnchor == "Low" ? 0 : firstAnchor == "High" ? 1 : -1;
                if (anchorIndex < 0)
                {
                    Fail($"Participant {participant}, delay {delay} has invalid first anchor {firstAnchor}.");
                }
                calibrationAnchorCounts[delayIndex, anchorIndex]++;
                string secondAnchor = PredictiveFlyExperiment2Controller.GetCalibrationAnchor(
                    participant,
                    delayOrderPosition,
                    1);
                if (firstAnchor == secondAnchor)
                {
                    Fail($"Participant {participant}, delay {delay} repeats the same calibration anchor.");
                }
            }

            if (participantConditions.Count != 6)
            {
                Fail($"Participant {participant} does not receive all six Experiment 2 conditions exactly once.");
            }
        }

        if (first500Count != 6 || first1000Count != 6)
        {
            Fail($"Delay order is not balanced: 500-first={first500Count}, 1000-first={first1000Count}.");
        }

        for (int condition = 0; condition < 6; condition++)
        {
            for (int route = 0; route < 4; route++)
            {
                if (conditionRouteCounts[condition, route] != 3)
                {
                    Fail($"Condition {condition}, route {route} count is {conditionRouteCounts[condition, route]}, expected 3.");
                }
            }
        }

        for (int block = 0; block < 2; block++)
        {
            if (blockOrderCounts[block].Count != 6)
            {
                Fail($"Block {block + 1} uses {blockOrderCounts[block].Count} strategy orders, expected 6.");
            }
            foreach (KeyValuePair<string, int> pair in blockOrderCounts[block])
            {
                if (pair.Value != 2)
                {
                    Fail($"Block {block + 1} strategy order {pair.Key} occurs {pair.Value} times, expected 2.");
                }
            }
        }

        for (int delayIndex = 0; delayIndex < 2; delayIndex++)
        {
            int delay = delayIndex == 0 ? 500 : 1000;
            if (delayOrderCounts[delayIndex].Count != 6)
            {
                Fail($"Delay {delay} ms uses {delayOrderCounts[delayIndex].Count} strategy orders, expected 6.");
            }
            foreach (KeyValuePair<string, int> pair in delayOrderCounts[delayIndex])
            {
                if (pair.Value != 2)
                {
                    Fail($"Delay {delay} ms strategy order {pair.Key} occurs {pair.Value} times, expected 2.");
                }
            }
        }

        for (int delayIndex = 0; delayIndex < 2; delayIndex++)
        {
            if (calibrationAnchorCounts[delayIndex, 0] != 6
                || calibrationAnchorCounts[delayIndex, 1] != 6)
            {
                Fail($"Calibration anchors are not balanced for delay index {delayIndex}: " +
                    $"low={calibrationAnchorCounts[delayIndex, 0]}, high={calibrationAnchorCounts[delayIndex, 1]}.");
            }
        }

        float twoRunSelection = PredictiveFlyExperiment2Controller.ComputeLockedHorizon(
            new[] { 0.2f, 0.6f }, 0f, 1f, 0.1f);
        if (!Mathf.Approximately(twoRunSelection, 0.4f))
        {
            Fail($"Two-run calibration lock returned {twoRunSelection}, expected 0.4.");
        }

        float threeRunSelection = PredictiveFlyExperiment2Controller.ComputeLockedHorizon(
            new[] { 0.1f, 0.9f, 0.4f }, 0f, 1f, 0.1f);
        if (!Mathf.Approximately(threeRunSelection, 0.4f))
        {
            Fail($"Three-run calibration lock returned {threeRunSelection}, expected 0.4.");
        }
    }

    static void ValidateOfficialScene()
    {
        AssetDatabase.ImportAsset(
            OfficialScenePath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != OfficialScenePath)
        {
            scene = EditorSceneManager.OpenScene(OfficialScenePath, OpenSceneMode.Single);
        }

        IrairaBou3DOfficialSceneBuilder builder = FindOfficialSceneBuilder(scene);
        if (builder == null)
        {
            Fail(BuildMissingBuilderMessage(scene));
        }

        PredictiveFlyExperiment2Controller experiment2 =
            builder.GetComponent<PredictiveFlyExperiment2Controller>();
        PredictiveFlyExperimentController experiment1 =
            builder.GetComponent<PredictiveFlyExperimentController>();
        PredictiveFlyObjectiveLogger logger = builder.GetComponent<PredictiveFlyObjectiveLogger>();

        if (builder.experimentProtocol != IrairaBou3DOfficialSceneBuilder.ExperimentProtocol.Experiment2)
        {
            Fail("Official scene is not configured for Experiment 2.");
        }
        if (experiment2 == null || !experiment2.enabled)
        {
            Fail("Experiment 2 controller is missing or disabled.");
        }
        if (experiment1 != null && experiment1.enabled)
        {
            Fail("Experiment 1 controller is still enabled.");
        }
        if (logger == null || !logger.saveIncompleteTrials)
        {
            Fail("Objective logger is missing or incomplete-trial saving is disabled.");
        }
        if (!experiment2.TryValidateStaticConfiguration(out string configurationMessage))
        {
            Fail($"Experiment 2 static configuration failed: {configurationMessage}");
        }
    }

    static void ValidateSyntheticConfiguration()
    {
        GameObject root = new GameObject("Experiment 2 Synthetic Validation");
        root.SetActive(false);
        try
        {
            IrairaBou3DOfficialSceneBuilder builder =
                root.AddComponent<IrairaBou3DOfficialSceneBuilder>();
            builder.buildOnEnable = false;
            builder.experimentProtocol =
                IrairaBou3DOfficialSceneBuilder.ExperimentProtocol.Experiment2;

            PredictiveGhostAvatarLocomotion locomotion =
                root.AddComponent<PredictiveGhostAvatarLocomotion>();
            PredictiveFlyObjectiveLogger logger =
                root.AddComponent<PredictiveFlyObjectiveLogger>();
            PredictiveFlyExperimentController experiment1 =
                root.AddComponent<PredictiveFlyExperimentController>();
            PredictiveFlyExperiment2Controller experiment2 =
                root.AddComponent<PredictiveFlyExperiment2Controller>();

            experiment1.enabled = false;
            experiment2.sceneBuilder = builder;
            experiment2.logger = logger;
            experiment2.locomotion = locomotion;
            experiment2.autoFindReferences = false;
            GameObject head = new GameObject("Synthetic Head");
            head.transform.SetParent(root.transform, false);
            IrairaBouTubeBoundary boundary = root.AddComponent<IrairaBouTubeBoundary>();
            boundary.Configure(new[] { Vector3.zero, Vector3.forward * 10f }, 2f);
            locomotion.targetRig = root.transform;
            locomotion.head = head.transform;
            locomotion.collisionProbe = head.transform;
            logger.autoFindReferences = false;
            logger.locomotion = locomotion;
            logger.rigRoot = root.transform;
            logger.head = head.transform;
            logger.probe = head.transform;
            logger.tubeBoundary = boundary;
            logger.saveIncompleteTrials = true;
            logger.outputDirectory = experiment2.objectiveOutputDirectory;
            logger.enabled = true;

            if (!experiment2.TryValidateStaticConfiguration(out string message))
            {
                Fail($"Synthetic Experiment 2 configuration failed: {message}");
            }
            if (!logger.saveIncompleteTrials || !logger.enabled)
            {
                Fail("Synthetic logger did not preserve the Experiment 2 incomplete-trial policy.");
            }
            if (experiment1.enabled)
            {
                Fail("Synthetic Experiment 1 controller remained enabled.");
            }

            ValidatePredictionProfiles(experiment2, locomotion);
            ValidateIncompleteObjectiveOutput(logger);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void ValidatePredictionProfiles(
        PredictiveFlyExperiment2Controller experiment2,
        PredictiveGhostAvatarLocomotion locomotion)
    {
        InvokeProfileMethod(experiment2, "ApplyCurrentPredictionProfile");
        AssertPredictionProfile(locomotion, 0f, 0f, 0f, 0f, "Current");

        InvokeProfileMethod(experiment2, "ApplyFixedPredictionProfile");
        AssertPredictionProfile(
            locomotion,
            PredictiveFlyExperiment2Controller.Experiment1MinTranslationHorizonSeconds,
            PredictiveFlyExperiment2Controller.Experiment1MaxTranslationHorizonSeconds,
            PredictiveFlyExperiment2Controller.Experiment1MinYawHorizonSeconds,
            PredictiveFlyExperiment2Controller.Experiment1MaxYawHorizonSeconds,
            "Fixed");

        MethodInfo personalized = typeof(PredictiveFlyExperiment2Controller).GetMethod(
            "ApplyPersonalizedPredictionProfile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (personalized == null)
        {
            Fail("ApplyPersonalizedPredictionProfile could not be found.");
        }
        personalized.Invoke(experiment2, new object[] { 0f });
        AssertPredictionProfile(locomotion, 0f, 0f, 0f, 0f, "Personalized zero");
    }

    static void InvokeProfileMethod(
        PredictiveFlyExperiment2Controller experiment2,
        string methodName)
    {
        MethodInfo method = typeof(PredictiveFlyExperiment2Controller).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            Fail($"{methodName} could not be found.");
        }
        method.Invoke(experiment2, null);
    }

    static void AssertPredictionProfile(
        PredictiveGhostAvatarLocomotion locomotion,
        float minTranslation,
        float maxTranslation,
        float minYaw,
        float maxYaw,
        string label)
    {
        if (!Mathf.Approximately(locomotion.minTranslationPredictionWindow, minTranslation)
            || !Mathf.Approximately(locomotion.maxTranslationPredictionWindow, maxTranslation)
            || !Mathf.Approximately(locomotion.minYawPredictionWindow, minYaw)
            || !Mathf.Approximately(locomotion.maxYawPredictionWindow, maxYaw))
        {
            Fail($"{label} prediction profile did not apply the expected windows.");
        }
    }

    static void ValidateIncompleteObjectiveOutput(PredictiveFlyObjectiveLogger logger)
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "PredictiveFlyExperiment2Validation",
            Guid.NewGuid().ToString("N"));
        try
        {
            logger.outputDirectory = testRoot;
            logger.incompleteTrialSubdirectory = "Incomplete";
            logger.participantId = "E2TEST";
            logger.trialNumber = 1;
            logger.conditionLabelOverride = "E2_D500_Personalized_H0p500";
            logger.StartLogging();
            if (!logger.IsLogging)
            {
                Fail("Synthetic objective logger did not start.");
            }

            logger.StopLogging(false);
            if (!logger.LastTrialDataSaved)
            {
                Fail($"Synthetic incomplete trial was not saved: {logger.LastSaveError}");
            }

            string incompleteDirectory = Path.Combine(testRoot, "Incomplete");
            string[] csvFiles = Directory.Exists(incompleteDirectory)
                ? Directory.GetFiles(incompleteDirectory, "*.csv")
                : Array.Empty<string>();
            if (csvFiles.Length != 4)
            {
                Fail($"Synthetic incomplete trial produced {csvFiles.Length} CSV files, expected 4.");
            }

            string summaryPath = Array.Find(
                csvFiles,
                path => path.EndsWith("_objective_trial_summary.csv", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(summaryPath))
            {
                Fail("Synthetic incomplete trial summary CSV is missing.");
            }

            string[] lines = File.ReadAllLines(summaryPath);
            if (lines.Length != 2)
            {
                Fail($"Synthetic summary contains {lines.Length} lines, expected 2.");
            }
            string[] headers = lines[0].Split(',');
            string[] values = lines[1].Split(',');
            int completedIndex = Array.IndexOf(headers, "completed");
            if (completedIndex < 0 || completedIndex >= values.Length || values[completedIndex] != "0")
            {
                Fail("Synthetic incomplete summary does not contain completed=0.");
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    static void Fail(string message)
    {
        throw new InvalidOperationException(message);
    }

    static IrairaBou3DOfficialSceneBuilder FindOfficialSceneBuilder(Scene scene)
    {
        IrairaBou3DOfficialSceneBuilder active =
            UnityEngine.Object.FindFirstObjectByType<IrairaBou3DOfficialSceneBuilder>();
        if (active != null && active.gameObject.scene == scene)
        {
            return active;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            IrairaBou3DOfficialSceneBuilder candidate =
                roots[i].GetComponentInChildren<IrairaBou3DOfficialSceneBuilder>(true);
            if (candidate != null)
            {
                return candidate;
            }
        }

        IrairaBou3DOfficialSceneBuilder[] loaded =
            Resources.FindObjectsOfTypeAll<IrairaBou3DOfficialSceneBuilder>();
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] != null && loaded[i].gameObject.scene == scene)
            {
                return loaded[i];
            }
        }
        return null;
    }

    static string BuildMissingBuilderMessage(Scene scene)
    {
        GameObject namedRoot = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == "IrairaBou3D Official Scene Builder")
            {
                namedRoot = roots[i];
                break;
            }
        }

        if (namedRoot == null)
        {
            return $"Official scene builder root was not found in {scene.path}; root count={roots.Length}.";
        }

        Component[] components = namedRoot.GetComponents<Component>();
        int missingScriptCount = 0;
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                missingScriptCount++;
            }
        }
        return $"Official scene builder root exists, but its builder component could not be loaded; " +
            $"component count={components.Length}, missing scripts={missingScriptCount}. " +
            DescribeScriptAsset("Assets/Scripts/IrairaBou/IrairaBou3DOfficialSceneBuilder.cs");
    }

    static string DescribeScriptAsset(string path)
    {
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (script == null)
        {
            return $"Script asset {path} did not load; imported GUID={guid}.";
        }

        Type type = script.GetClass();
        return $"Script asset GUID={guid}, class={(type == null ? "null" : type.FullName)}.";
    }
}
#endif
