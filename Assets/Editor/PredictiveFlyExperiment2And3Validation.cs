#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PredictiveFlyExperiment2And3Validation
{
    const string OfficialScenePath = "Assets/Scenes/IrairaBou3D_Official.unity";

    [MenuItem("PredictiveFly/Experiments 2 and 3/Install In Official Scene")]
    public static void InstallInOfficialScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play Mode before installing Experiments 2 and 3.");
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

        PredictiveFlyExperiment3Controller experiment3 =
            builder.GetComponent<PredictiveFlyExperiment3Controller>();
        if (experiment3 == null)
        {
            experiment3 = builder.gameObject.AddComponent<PredictiveFlyExperiment3Controller>();
        }

        PredictiveFlyExperiment2Controller experiment2 =
            builder.GetComponent<PredictiveFlyExperiment2Controller>();
        if (experiment2 == null)
        {
            experiment2 = builder.gameObject.AddComponent<PredictiveFlyExperiment2Controller>();
        }

        PredictiveGhostAvatarLocomotion locomotion =
            UnityEngine.Object.FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();

        experiment2.sceneBuilder = builder;
        experiment2.locomotion = locomotion;
        experiment2.experiment3Controller = experiment3;
        experiment2.calibrationOutputDirectory =
            PredictiveFlyExperiment2CalibrationStore.DefaultCalibrationOutputDirectory;
        experiment3.sceneBuilder = builder;
        experiment3.logger = logger;
        experiment3.locomotion = locomotion;
        experiment3.experiment2Controller = experiment2;
        experiment3.calibrationOutputDirectory = experiment2.calibrationOutputDirectory;
        experiment3.objectiveOutputDirectory = "Data/PredictiveFlyExperiment3/Objective";

        logger.useKeyboardControls = false;
        logger.enabled = true;
        logger.stopOnFinish = true;
        logger.stopOnModeChange = true;
        logger.saveIncompleteTrials = false;
        logger.incompleteTrialSubdirectory = "Incomplete";
        logger.outputDirectory = experiment3.objectiveOutputDirectory;

        EditorUtility.SetDirty(experiment2);
        EditorUtility.SetDirty(experiment3);
        EditorUtility.SetDirty(logger);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Official scene could not be saved.");
        }

        Debug.Log("[PredictiveFly] Experiment 2 and 3 controllers installed without changing any existing controller enabled state.");
    }

    [MenuItem("PredictiveFly/Experiments 2 and 3/Validate Schedule And Scene")]
    public static void ValidateScheduleAndScene()
    {
        ValidateSchedule();
        ValidateOfficialScene();
        Debug.Log("[PredictiveFly] Experiments 2 and 3 validation passed.");
    }

    public static void InstallAndValidateBatch()
    {
        try
        {
            ValidateSchedule();
            InstallInOfficialScene();
            ValidateOfficialScene();
            AssetDatabase.SaveAssets();
            Debug.Log("EXPERIMENTS2AND3_VALIDATION_PASSED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("EXPERIMENTS2AND3_VALIDATION_FAILED");
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
            Debug.Log("EXPERIMENTS2AND3_LOGIC_VALIDATION_PASSED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("EXPERIMENTS2AND3_LOGIC_VALIDATION_FAILED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            throw;
        }
    }

    static void ValidateSchedule()
    {
        const int participantCount = 12;
        string[] expectedQuestionnaireCodes =
        {
            "A_1", "A_2", "B_1", "B_2", "C_1", "C_2"
        };
        int[] firstDelayCounts = new int[PredictiveFlyExperiment3Controller.DelayBlockCount];
        int[,] conditionRouteCounts = new int[PredictiveFlyExperiment3Controller.FormalTrialCount, 4];
        int[,] conditionPositionCounts = new int[
            PredictiveFlyExperiment3Controller.FormalTrialCount,
            PredictiveFlyExperiment3Controller.FormalTrialCount];
        Dictionary<string, int>[] blockStrategyOrderCounts =
            new Dictionary<string, int>[PredictiveFlyExperiment3Controller.DelayBlockCount];
        Dictionary<string, int>[] delayStrategyOrderCounts =
            new Dictionary<string, int>[PredictiveFlyExperiment3Controller.DelayBlockCount];
        for (int i = 0; i < PredictiveFlyExperiment3Controller.DelayBlockCount; i++)
        {
            blockStrategyOrderCounts[i] = new Dictionary<string, int>();
            delayStrategyOrderCounts[i] = new Dictionary<string, int>();
        }
        Dictionary<string, int> fullOrderCounts = new Dictionary<string, int>();

        for (int participant = 1; participant <= participantCount; participant++)
        {
            int firstDelay = PredictiveFlyExperiment3Controller.GetFirstDelayMilliseconds(participant);
            int firstDelayIndex = PredictiveFlyExperiment3Controller.GetDelayLevelIndex(firstDelay);
            if (firstDelayIndex < 0)
            {
                Fail($"Participant {participant} has invalid first delay {firstDelay}.");
            }
            firstDelayCounts[firstDelayIndex]++;

            HashSet<int> participantConditions = new HashSet<int>();
            HashSet<string> participantQuestionnaireCodes = new HashSet<string>();
            int[] participantRouteCounts = new int[4];
            string fullOrder = string.Empty;
            for (int block = 0; block < PredictiveFlyExperiment3Controller.DelayBlockCount; block++)
            {
                HashSet<PredictiveFlyExperiment3Controller.ProxyStrategy> strategies =
                    new HashSet<PredictiveFlyExperiment3Controller.ProxyStrategy>();
                string order = string.Empty;
                int blockDelay = -1;
                for (int position = 0;
                    position < PredictiveFlyExperiment3Controller.StrategiesPerDelay;
                    position++)
                {
                    int trial = block * PredictiveFlyExperiment3Controller.StrategiesPerDelay
                        + position
                        + 1;
                    if (!PredictiveFlyExperiment3Controller.TryGetTrialAssignment(
                            participant,
                            trial,
                            out PredictiveFlyExperiment3Controller.TrialAssignment assignment))
                    {
                        Fail($"Participant {participant}, trial {trial} did not resolve.");
                    }

                    blockDelay = blockDelay < 0 ? assignment.delayMilliseconds : blockDelay;
                    if (assignment.delayMilliseconds != blockDelay)
                    {
                        Fail($"Participant {participant}, block {block + 1} mixes delay levels.");
                    }
                    if (assignment.strategy != PredictiveFlyExperiment3Controller.ProxyStrategy.Current
                        && assignment.strategy != PredictiveFlyExperiment3Controller.ProxyStrategy.Personalized)
                    {
                        Fail($"Participant {participant}, trial {trial} has an invalid strategy.");
                    }
                    if (assignment.canonicalConditionCode < 0
                        || assignment.canonicalConditionCode
                            >= PredictiveFlyExperiment3Controller.FormalTrialCount)
                    {
                        Fail($"Participant {participant}, trial {trial} has invalid condition code "
                            + $"{assignment.canonicalConditionCode}.");
                    }
                    strategies.Add(assignment.strategy);
                    participantConditions.Add(assignment.canonicalConditionCode);
                    string questionnaireCode =
                        PredictiveFlyExperiment3Controller.GetQuestionnaireModeCode(assignment);
                    if (questionnaireCode
                        != expectedQuestionnaireCodes[assignment.canonicalConditionCode])
                    {
                        Fail($"Participant {participant}, trial {trial} maps condition "
                            + $"{assignment.canonicalConditionCode} to questionnaire code "
                            + $"'{questionnaireCode}', expected "
                            + $"'{expectedQuestionnaireCodes[assignment.canonicalConditionCode]}'.");
                    }
                    participantQuestionnaireCodes.Add(questionnaireCode);
                    participantRouteCounts[(int)assignment.route]++;
                    conditionRouteCounts[assignment.canonicalConditionCode, (int)assignment.route]++;
                    conditionPositionCounts[assignment.canonicalConditionCode, trial - 1]++;
                    order += ((int)assignment.strategy).ToString();
                    fullOrder += assignment.canonicalConditionCode.ToString();
                }

                if (strategies.Count != PredictiveFlyExperiment3Controller.StrategiesPerDelay)
                {
                    Fail($"Participant {participant}, block {block + 1} does not contain both strategies.");
                }
                blockStrategyOrderCounts[block][order] = blockStrategyOrderCounts[block].TryGetValue(order, out int count)
                    ? count + 1
                    : 1;
                int delayIndex = PredictiveFlyExperiment3Controller.GetDelayLevelIndex(blockDelay);
                if (delayIndex < 0)
                {
                    Fail($"Participant {participant}, block {block + 1} uses invalid delay {blockDelay}.");
                }
                delayStrategyOrderCounts[delayIndex][order] =
                    delayStrategyOrderCounts[delayIndex].TryGetValue(order, out int strategyOrderCount)
                        ? strategyOrderCount + 1
                        : 1;

                int delay = PredictiveFlyExperiment3Controller.GetDelayForOrderPosition(
                    participant,
                    block);
                if (delay != blockDelay)
                {
                    Fail($"Participant {participant}, block {block + 1} uses {blockDelay} ms, expected {delay} ms.");
                }
            }

            if (participantConditions.Count != PredictiveFlyExperiment3Controller.FormalTrialCount)
            {
                Fail($"Participant {participant} does not receive all six Experiment 3 conditions exactly once.");
            }
            if (participantQuestionnaireCodes.Count
                != PredictiveFlyExperiment3Controller.FormalTrialCount)
            {
                Fail($"Participant {participant} does not receive all six questionnaire mode codes exactly once.");
            }
            for (int route = 0; route < participantRouteCounts.Length; route++)
            {
                if (participantRouteCounts[route] < 1 || participantRouteCounts[route] > 2)
                {
                    Fail($"Participant {participant}, route {route} occurs {participantRouteCounts[route]} times; expected one or two uses.");
                }
            }
            if (PredictiveFlyExperiment3Controller.TryGetTrialAssignment(
                    participant,
                    PredictiveFlyExperiment3Controller.FormalTrialCount + 1,
                    out _))
            {
                Fail($"Participant {participant} resolved an out-of-range seventh formal trial.");
            }
            fullOrderCounts[fullOrder] = fullOrderCounts.TryGetValue(fullOrder, out int fullCount)
                ? fullCount + 1
                : 1;
        }

        for (int delayIndex = 0; delayIndex < firstDelayCounts.Length; delayIndex++)
        {
            if (firstDelayCounts[delayIndex] != 4)
            {
                Fail($"Delay {PredictiveFlyExperiment3Controller.GetDelayLevelMilliseconds(delayIndex)} ms is first "
                    + $"{firstDelayCounts[delayIndex]} times, expected 4.");
            }
        }

        for (int condition = 0;
            condition < PredictiveFlyExperiment3Controller.FormalTrialCount;
            condition++)
        {
            for (int route = 0; route < 4; route++)
            {
                if (conditionRouteCounts[condition, route] != 3)
                {
                    Fail($"Condition {condition}, route {route} count is {conditionRouteCounts[condition, route]}, expected 3.");
                }
            }
            for (int position = 0;
                position < PredictiveFlyExperiment3Controller.FormalTrialCount;
                position++)
            {
                if (conditionPositionCounts[condition, position] != 2)
                {
                    Fail($"Condition {condition}, position {position + 1} count is "
                        + $"{conditionPositionCounts[condition, position]}, expected 2.");
                }
            }
        }

        for (int block = 0; block < PredictiveFlyExperiment3Controller.DelayBlockCount; block++)
        {
            if (blockStrategyOrderCounts[block].Count != 2)
            {
                Fail($"Block {block + 1} uses {blockStrategyOrderCounts[block].Count} strategy orders, expected 2.");
            }
            foreach (KeyValuePair<string, int> pair in blockStrategyOrderCounts[block])
            {
                if (pair.Value != 6)
                {
                    Fail($"Block {block + 1} strategy order {pair.Key} occurs {pair.Value} times, expected 6.");
                }
            }
        }

        for (int delayIndex = 0; delayIndex < PredictiveFlyExperiment3Controller.DelayBlockCount; delayIndex++)
        {
            int delay = PredictiveFlyExperiment3Controller.GetDelayLevelMilliseconds(delayIndex);
            if (delayStrategyOrderCounts[delayIndex].Count != 2)
            {
                Fail($"Delay {delay} ms uses {delayStrategyOrderCounts[delayIndex].Count} strategy orders, expected 2.");
            }
            foreach (KeyValuePair<string, int> pair in delayStrategyOrderCounts[delayIndex])
            {
                if (pair.Value != 6)
                {
                    Fail($"Delay {delay} ms strategy order {pair.Key} occurs {pair.Value} times, expected 6.");
                }
            }
        }

        if (fullOrderCounts.Count != participantCount)
        {
            Fail($"The base schedule uses {fullOrderCounts.Count} distinct full condition orders, expected {participantCount}.");
        }

        ValidateExperiment2Schedule();

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

    static void ValidateExperiment2Schedule()
    {
        const int baseCycleParticipants = 10;
        int[,] positionCounts = new int[
            PredictiveFlyExperiment2Controller.CalibrationDelayCount,
            PredictiveFlyExperiment2Controller.CalibrationDelayCount];
        int[,] transitionCounts = new int[
            PredictiveFlyExperiment2Controller.CalibrationDelayCount,
            PredictiveFlyExperiment2Controller.CalibrationDelayCount];
        int[,] anchorCounts = new int[
            PredictiveFlyExperiment2Controller.CalibrationDelayCount,
            2];
        HashSet<string> uniqueOrders = new HashSet<string>();

        for (int participant = 1; participant <= baseCycleParticipants; participant++)
        {
            HashSet<int> delays = new HashSet<int>();
            StringBuilder order = new StringBuilder(32);
            int previousDelayIndex = -1;
            for (int position = 0;
                position < PredictiveFlyExperiment2Controller.CalibrationDelayCount;
                position++)
            {
                int delay = PredictiveFlyExperiment2Controller.GetCalibrationDelayForOrderPosition(
                    participant,
                    position);
                int delayIndex = PredictiveFlyExperiment2Controller.GetCalibrationDelayLevelIndex(delay);
                if (delayIndex < 0 || !delays.Add(delay))
                {
                    Fail($"Experiment 2 participant {participant} has an invalid or repeated delay {delay}.");
                }

                positionCounts[delayIndex, position]++;
                if (previousDelayIndex >= 0)
                {
                    transitionCounts[previousDelayIndex, delayIndex]++;
                }
                previousDelayIndex = delayIndex;

                string firstAnchor = PredictiveFlyExperiment2Controller.GetCalibrationAnchor(
                    participant,
                    position,
                    0);
                string secondAnchor = PredictiveFlyExperiment2Controller.GetCalibrationAnchor(
                    participant,
                    position,
                    1);
                int firstAnchorIndex = firstAnchor == "Low" ? 0 : firstAnchor == "High" ? 1 : -1;
                if (firstAnchorIndex < 0 || firstAnchor == secondAnchor)
                {
                    Fail($"Experiment 2 participant {participant}, delay {delay} has invalid anchors.");
                }
                anchorCounts[delayIndex, firstAnchorIndex]++;

                if (position > 0)
                {
                    order.Append('>');
                }
                order.Append(delay);
            }

            if (delays.Count != PredictiveFlyExperiment2Controller.CalibrationDelayCount)
            {
                Fail($"Experiment 2 participant {participant} does not receive all five delays.");
            }
            uniqueOrders.Add(order.ToString());
        }

        if (uniqueOrders.Count != baseCycleParticipants)
        {
            Fail($"Experiment 2 has {uniqueOrders.Count} unique orders; expected 10.");
        }

        for (int delay = 0; delay < PredictiveFlyExperiment2Controller.CalibrationDelayCount; delay++)
        {
            for (int position = 0;
                position < PredictiveFlyExperiment2Controller.CalibrationDelayCount;
                position++)
            {
                if (positionCounts[delay, position] != 2)
                {
                    Fail($"Experiment 2 delay index {delay}, position {position} occurs "
                        + $"{positionCounts[delay, position]} times; expected 2.");
                }
            }
            if (anchorCounts[delay, 0] != 5 || anchorCounts[delay, 1] != 5)
            {
                Fail($"Experiment 2 anchors are not balanced for delay index {delay}.");
            }

            for (int nextDelay = 0;
                nextDelay < PredictiveFlyExperiment2Controller.CalibrationDelayCount;
                nextDelay++)
            {
                int expected = delay == nextDelay ? 0 : 2;
                if (transitionCounts[delay, nextDelay] != expected)
                {
                    Fail($"Experiment 2 transition {delay}->{nextDelay} occurs "
                        + $"{transitionCounts[delay, nextDelay]} times; expected {expected}.");
                }
            }
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

        PredictiveFlyExperiment3Controller experiment2 =
            builder.GetComponent<PredictiveFlyExperiment3Controller>();
        PredictiveFlyExperiment2Controller calibration =
            builder.GetComponent<PredictiveFlyExperiment2Controller>();
        PredictiveFlyObjectiveLogger logger = builder.GetComponent<PredictiveFlyObjectiveLogger>();

        if (experiment2 == null)
        {
            Fail("Experiment 3 controller is missing.");
        }
        if (calibration == null)
        {
            Fail("Experiment 2 preference controller is missing.");
        }
        if (experiment2.experiment2Controller != calibration
            || calibration.experiment3Controller != experiment2)
        {
            Fail("Experiment 2 and Experiment 3 controllers are not cross-linked.");
        }
        if (logger == null || logger.saveIncompleteTrials)
        {
            Fail("Objective logger is missing or incomplete Experiment 3 trials would be retained.");
        }
        if (!calibration.TryValidateStaticConfiguration(out string experiment2Message))
        {
            Fail($"Experiment 2 static configuration failed: {experiment2Message}");
        }
        if (!experiment2.TryValidateStaticConfiguration(out string experiment3Message))
        {
            Fail($"Experiment 3 static configuration failed: {experiment3Message}");
        }
        if (experiment2.locomotion == null || experiment2.locomotion.maxPredictionDistance > 0f)
        {
            Fail("Experiments 2 and 3 must disable the predictive proxy distance cap (Max Prediction Distance = 0).");
        }
    }

    static void ValidateSyntheticConfiguration()
    {
        GameObject root = new GameObject("Experiments 2 and 3 Synthetic Validation");
        root.SetActive(false);
        try
        {
            IrairaBou3DOfficialSceneBuilder builder =
                root.AddComponent<IrairaBou3DOfficialSceneBuilder>();
            builder.buildOnEnable = false;

            PredictiveGhostAvatarLocomotion locomotion =
                root.AddComponent<PredictiveGhostAvatarLocomotion>();
            PredictiveFlyObjectiveLogger logger =
                root.AddComponent<PredictiveFlyObjectiveLogger>();
            PredictiveFlyExperiment3Controller experiment2 =
                root.AddComponent<PredictiveFlyExperiment3Controller>();
            PredictiveFlyExperiment2Controller calibration =
                root.AddComponent<PredictiveFlyExperiment2Controller>();

            experiment2.sceneBuilder = builder;
            experiment2.logger = logger;
            experiment2.locomotion = locomotion;
            experiment2.experiment2Controller = calibration;
            experiment2.autoFindReferences = false;
            calibration.sceneBuilder = builder;
            calibration.locomotion = locomotion;
            calibration.experiment3Controller = experiment2;
            calibration.autoFindReferences = false;
            calibration.calibrationOutputDirectory = experiment2.calibrationOutputDirectory;
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
            logger.saveIncompleteTrials = false;
            logger.outputDirectory = experiment2.objectiveOutputDirectory;
            logger.enabled = true;

            if (!experiment2.TryValidateStaticConfiguration(out string message))
            {
                Fail($"Synthetic Experiment 3 configuration failed: {message}");
            }
            if (!calibration.TryValidateStaticConfiguration(out string calibrationMessage))
            {
                Fail($"Synthetic Experiment 2 configuration failed: {calibrationMessage}");
            }
            if (logger.saveIncompleteTrials || !logger.enabled)
            {
                Fail("Synthetic logger did not preserve the Experiment 3 completed-only policy.");
            }
            ValidatePredictionProfiles(experiment2, locomotion);
            ValidateFactorialConditionSemantics(experiment2, locomotion);
            ValidateCompletedCalibrationGate(experiment2, calibration);
            ValidateIncompleteObjectiveDiscard(logger);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void ValidatePredictionProfiles(
        PredictiveFlyExperiment3Controller experiment2,
        PredictiveGhostAvatarLocomotion locomotion)
    {
        InvokeProfileMethod(experiment2, "ApplyCurrentPredictionProfile");
        AssertPredictionProfile(locomotion, 0f, 0f, 0f, 0f, "Current");

        MethodInfo personalized = typeof(PredictiveFlyExperiment3Controller).GetMethod(
            "ApplyPersonalizedPredictionProfile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (personalized == null)
        {
            Fail("ApplyPersonalizedPredictionProfile could not be found.");
        }
        personalized.Invoke(experiment2, new object[] { 0f });
        AssertPredictionProfile(locomotion, 0f, 0f, 0f, 0f, "Personalized zero");

        personalized.Invoke(
            experiment2,
            new object[] { PredictiveFlyExperiment3Controller.Experiment1MaxTranslationHorizonSeconds });
        AssertPredictionProfile(
            locomotion,
            PredictiveFlyExperiment3Controller.Experiment1MinTranslationHorizonSeconds,
            PredictiveFlyExperiment3Controller.Experiment1MaxTranslationHorizonSeconds,
            PredictiveFlyExperiment3Controller.Experiment1MinYawHorizonSeconds,
            PredictiveFlyExperiment3Controller.Experiment1MaxYawHorizonSeconds,
            "Personalized at Experiment 1 reference horizon");
    }

    static void ValidateFactorialConditionSemantics(
        PredictiveFlyExperiment3Controller experiment2,
        PredictiveGhostAvatarLocomotion locomotion)
    {
        MethodInfo applyAssignment = typeof(PredictiveFlyExperiment3Controller).GetMethod(
            "ApplyTrialAssignment",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (applyAssignment == null)
        {
            Fail("ApplyTrialAssignment could not be found.");
        }

        int[] delays = { 0, 500, 1000 };
        for (int i = 0; i < delays.Length; i++)
        {
            int delay = delays[i];
            SetSelectedHorizon(experiment2, delay, 0f);
            ApplySyntheticAssignment(
                applyAssignment,
                experiment2,
                delay,
                PredictiveFlyExperiment3Controller.ProxyStrategy.Current);
            AssertConditionState(
                locomotion,
                delay,
                delay == 0
                    ? PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody
                    : PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost,
                0f,
                $"D{delay} Current");

            ApplySyntheticAssignment(
                applyAssignment,
                experiment2,
                delay,
                PredictiveFlyExperiment3Controller.ProxyStrategy.Personalized);
            AssertConditionState(
                locomotion,
                delay,
                delay == 0
                    ? PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody
                    : PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost,
                0f,
                $"D{delay} Personalized H=0");

            SetSelectedHorizon(experiment2, delay, 0.4f);
            ApplySyntheticAssignment(
                applyAssignment,
                experiment2,
                delay,
                PredictiveFlyExperiment3Controller.ProxyStrategy.Personalized);
            AssertConditionState(
                locomotion,
                delay,
                PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost,
                0.4f,
                $"D{delay} Personalized H=0.4");
        }
    }

    static void SetSelectedHorizon(
        PredictiveFlyExperiment3Controller experiment2,
        int delayMilliseconds,
        float value)
    {
        string fieldName;
        switch (delayMilliseconds)
        {
            case 0:
                fieldName = "selectedHorizon0Seconds";
                break;
            case 500:
                fieldName = "selectedHorizon500Seconds";
                break;
            case 1000:
                fieldName = "selectedHorizon1000Seconds";
                break;
            default:
                Fail($"Unsupported synthetic delay {delayMilliseconds}.");
                return;
        }

        FieldInfo field = typeof(PredictiveFlyExperiment3Controller).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            Fail($"Synthetic calibration field {fieldName} could not be found.");
        }
        field.SetValue(experiment2, value);
    }

    static void ApplySyntheticAssignment(
        MethodInfo applyAssignment,
        PredictiveFlyExperiment3Controller experiment2,
        int delayMilliseconds,
        PredictiveFlyExperiment3Controller.ProxyStrategy strategy)
    {
        applyAssignment.Invoke(
            experiment2,
            new object[]
            {
                new PredictiveFlyExperiment3Controller.TrialAssignment
                {
                    delayMilliseconds = delayMilliseconds,
                    strategy = strategy
                }
            });
    }

    static void AssertConditionState(
        PredictiveGhostAvatarLocomotion locomotion,
        int expectedDelayMilliseconds,
        PredictiveGhostAvatarLocomotion.GhostVisualizationMode expectedMode,
        float expectedMaximumTranslationHorizon,
        string label)
    {
        if (!locomotion.enableInputToRigDelay
            || locomotion.inputToRigDelayMilliseconds != expectedDelayMilliseconds
            || locomotion.visualizationMode != expectedMode
            || !Mathf.Approximately(
                locomotion.maxTranslationPredictionWindow,
                expectedMaximumTranslationHorizon))
        {
            Fail($"{label} did not apply its expected delay, visualization, and prediction horizon.");
        }
    }

    static void InvokeProfileMethod(
        PredictiveFlyExperiment3Controller experiment2,
        string methodName)
    {
        MethodInfo method = typeof(PredictiveFlyExperiment3Controller).GetMethod(
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

    static void ValidateCompletedCalibrationGate(
        PredictiveFlyExperiment3Controller experiment2,
        PredictiveFlyExperiment2Controller calibration)
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "PredictiveFlyExperiment2CalibrationGate",
            Guid.NewGuid().ToString("N"));
        const string participant = "GATE001";
        try
        {
            experiment2.participantId = participant;
            experiment2.calibrationOutputDirectory = testRoot;
            calibration.participantId = participant;
            calibration.calibrationOutputDirectory = testRoot;

            if (experiment2.TryRefreshCalibrationFromDisk(out string missingMessage))
            {
                Fail("Experiment 3 accepted a participant with no completed Experiment 2 profile.");
            }
            if (missingMessage.IndexOf("Calibration incomplete", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Fail($"Missing calibration did not produce an explicit incomplete message: {missingMessage}");
            }

            Directory.CreateDirectory(testRoot);
            string csvFileName = $"{participant}_synthetic_calibration.csv";
            string csvPath = Path.Combine(testRoot, csvFileName);
            File.WriteAllText(
                csvPath,
                "protocol_version,timestamp,event_type,participant_id\n"
                + $"{PredictiveFlyExperiment2CalibrationStore.ProtocolVersion},synthetic,sequence_complete,{participant}\n");

            PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile profile =
                new PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile
                {
                    protocolVersion = PredictiveFlyExperiment2CalibrationStore.ProtocolVersion,
                    completed = true,
                    participantId = participant,
                    completedAt = DateTime.UtcNow.ToString("o"),
                    calibrationCsvFileName = csvFileName,
                    selectedHorizon0Seconds = 0.3f,
                    selectedHorizon250Seconds = 0.3f,
                    selectedHorizon500Seconds = 0.2f,
                    selectedHorizon750Seconds = 0.1f,
                    selectedHorizon1000Seconds = 0f,
                    minimumSelectableHorizonSeconds = 0f,
                    maximumSelectableHorizonSeconds = 1f,
                    horizonStepSeconds = 0.1f,
                    referenceMinTranslationHorizonSeconds =
                        PredictiveFlyExperiment3Controller.Experiment1MinTranslationHorizonSeconds,
                    referenceMaxTranslationHorizonSeconds =
                        PredictiveFlyExperiment3Controller.Experiment1MaxTranslationHorizonSeconds,
                    referenceMinYawHorizonSeconds =
                        PredictiveFlyExperiment3Controller.Experiment1MinYawHorizonSeconds,
                    referenceMaxYawHorizonSeconds =
                        PredictiveFlyExperiment3Controller.Experiment1MaxYawHorizonSeconds,
                    predictionMethod = (int)PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited
                };
            if (!PredictiveFlyExperiment2CalibrationStore.TrySaveCompletedProfile(
                    profile,
                    testRoot,
                    out _,
                    out string saveMessage))
            {
                Fail($"Synthetic completed calibration profile could not be saved: {saveMessage}");
            }
            if (!experiment2.TryRefreshCalibrationFromDisk(out string readyMessage)
                || !experiment2.CalibrationReady)
            {
                Fail($"Experiment 3 rejected a valid completed Experiment 2 profile: {readyMessage}");
            }

            File.Delete(csvPath);
            if (experiment2.TryRefreshCalibrationFromDisk(out string missingCsvMessage))
            {
                Fail("Experiment 3 accepted an Experiment 2 profile whose calibration CSV was missing.");
            }
            if (missingCsvMessage.IndexOf("CSV is missing", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Fail($"Missing calibration CSV did not produce the expected message: {missingCsvMessage}");
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

    static void ValidateIncompleteObjectiveDiscard(PredictiveFlyObjectiveLogger logger)
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "PredictiveFlyExperiment2And3Validation",
            Guid.NewGuid().ToString("N"));
        try
        {
            logger.outputDirectory = testRoot;
            logger.incompleteTrialSubdirectory = "Incomplete";
            logger.saveIncompleteTrials = false;
            logger.participantId = "E3TEST";
            logger.trialNumber = 1;
            logger.conditionLabelOverride = "E3_D500_Personalized_H0p500";
            logger.StartLogging();
            if (!logger.IsLogging)
            {
                Fail("Synthetic objective logger did not start.");
            }

            logger.StopLogging(false);
            if (logger.LastTrialDataSaved)
            {
                Fail("Synthetic incomplete Experiment 3 trial was unexpectedly saved.");
            }

            string[] csvFiles = Directory.Exists(testRoot)
                ? Directory.GetFiles(testRoot, "*.csv", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (csvFiles.Length != 0)
            {
                Fail($"Synthetic incomplete Experiment 3 trial produced {csvFiles.Length} CSV files; expected none.");
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
