﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Runtime;
using Microsoft.ML.TestFramework;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.AutoML.Test
{
    public class AutoMLExperimentTests : BaseTestClass
    {
        public AutoMLExperimentTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task AutoMLExperiment_throw_timeout_exception_when_ct_is_canceled_and_no_trial_completed_Async()
        {
            var context = new MLContext(1);
            var experiment = context.Auto().CreateExperiment();

            experiment.SetTrainingTimeInSeconds(1)
                      .SetTrialRunner((serviceProvider) =>
                      {
                          var channel = serviceProvider.GetService<IChannel>();
                          var settings = serviceProvider.GetService<AutoMLExperiment.AutoMLExperimentSettings>();
                          return new DummyTrialRunner(settings, 5, channel);
                      })
                      .SetTuner<RandomSearchTuner>();

            var cts = new CancellationTokenSource();

            context.Log += (o, e) =>
            {
                if (e.RawMessage.Contains("Update Running Trial"))
                {
                    cts.Cancel();
                }
            };

            var runExperimentAction = async () => await experiment.RunAsync(cts.Token);

            await runExperimentAction.Should().ThrowExactlyAsync<TimeoutException>();
        }

        [Fact]
        public async Task AutoMLExperiment_return_current_best_trial_when_ct_is_canceled_with_trial_completed_Async()
        {
            var context = new MLContext(1);
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var experiment = context.Auto().CreateExperiment();
            experiment.SetTrainingTimeInSeconds(10)
                      .SetTrialRunner((serviceProvider) =>
                      {
                          var channel = serviceProvider.GetService<IChannel>();
                          var settings = serviceProvider.GetService<AutoMLExperiment.AutoMLExperimentSettings>();
                          return new DummyTrialRunner(settings, 1, channel);
                      })
                      .SetTuner<RandomSearchTuner>();

            var cts = new CancellationTokenSource();

            context.Log += (o, e) =>
            {
                if (e.RawMessage.Contains("Update Completed Trial"))
                {
                    cts.CancelAfter(100);
                }
            };
            var res = await experiment.RunAsync(cts.Token);

            stopWatch.Stop();
            stopWatch.ElapsedMilliseconds.Should().BeLessOrEqualTo(2 * 1000);
            cts.IsCancellationRequested.Should().BeTrue();
            res.Metric.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task AutoMLExperiment_finish_training_when_time_is_up_Async()
        {
            var context = new MLContext(1);

            var experiment = context.Auto().CreateExperiment();
            experiment.SetTrainingTimeInSeconds(5)
                      .SetTrialRunner((serviceProvider) =>
                      {
                          var channel = serviceProvider.GetService<IChannel>();
                          var settings = serviceProvider.GetService<AutoMLExperiment.AutoMLExperimentSettings>();
                          return new DummyTrialRunner(settings, 1, channel);
                      })
                      .SetTuner<RandomSearchTuner>();

            var cts = new CancellationTokenSource();
            cts.CancelAfter(10 * 1000);

            var res = await experiment.RunAsync(cts.Token);
            res.Metric.Should().BeGreaterThan(0);
            cts.IsCancellationRequested.Should().BeFalse();
        }

        [Fact]
        public async Task AutoMLExperiment_UCI_Adult_Train_Test_Split_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var data = DatasetUtil.GetUciAdultDataView();
            var experiment = context.Auto().CreateExperiment();
            var pipeline = context.Auto().Featurizer(data, "_Features_", excludeColumns: new[] { DatasetUtil.UciAdultLabel })
                                .Append(context.Auto().BinaryClassification(DatasetUtil.UciAdultLabel, "_Features_", useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(context.Data.TrainTestSplit(data))
                    .SetBinaryClassificationMetric(BinaryClassificationMetric.AreaUnderRocCurve, DatasetUtil.UciAdultLabel)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(1);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task AutoMLExperiment_UCI_Adult_CV_5_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var data = DatasetUtil.GetUciAdultDataView();
            var experiment = context.Auto().CreateExperiment();
            var pipeline = context.Auto().Featurizer(data, "_Features_", excludeColumns: new[] { DatasetUtil.UciAdultLabel })
                                .Append(context.Auto().BinaryClassification(DatasetUtil.UciAdultLabel, "_Features_", useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(data, 5)
                    .SetBinaryClassificationMetric(BinaryClassificationMetric.AreaUnderRocCurve, DatasetUtil.UciAdultLabel)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(10);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task AutoMLExperiment_Iris_CV_5_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var data = DatasetUtil.GetIrisDataView();
            var experiment = context.Auto().CreateExperiment();
            var label = "Label";
            var pipeline = context.Auto().Featurizer(data, excludeColumns: new[] { label })
                                .Append(context.Transforms.Conversion.MapValueToKey(label, label))
                                .Append(context.Auto().MultiClassification(label, useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(data, 5)
                    .SetMulticlassClassificationMetric(MulticlassClassificationMetric.MacroAccuracy, label)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(10);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task AutoMLExperiment_Iris_Train_Test_Split_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var data = DatasetUtil.GetIrisDataView();
            var experiment = context.Auto().CreateExperiment();
            var label = "Label";
            var pipeline = context.Auto().Featurizer(data, excludeColumns: new[] { label })
                                .Append(context.Transforms.Conversion.MapValueToKey(label, label))
                                .Append(context.Auto().MultiClassification(label, useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(context.Data.TrainTestSplit(data))
                    .SetMulticlassClassificationMetric(MulticlassClassificationMetric.MacroAccuracy, label)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(10);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.8);
        }

        [Fact]
        public async Task AutoMLExperiment_Taxi_Fare_Train_Test_Split_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var train = DatasetUtil.GetTaxiFareTrainDataView();
            var test = DatasetUtil.GetTaxiFareTestDataView();
            var experiment = context.Auto().CreateExperiment();
            var label = DatasetUtil.TaxiFareLabel;
            var pipeline = context.Auto().Featurizer(train, excludeColumns: new[] { label })
                                .Append(context.Auto().Regression(label, useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(train, test)
                    .SetRegressionMetric(RegressionMetric.RSquared, label)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(50);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.5);
        }

        [Fact]
        public async Task AutoMLExperiment_Taxi_Fare_CV_5_Test()
        {
            var context = new MLContext(1);
            context.Log += (o, e) =>
            {
                if (e.Source.StartsWith("AutoMLExperiment"))
                {
                    this.Output.WriteLine(e.RawMessage);
                }
            };
            var train = DatasetUtil.GetTaxiFareTrainDataView();
            var experiment = context.Auto().CreateExperiment();
            var label = DatasetUtil.TaxiFareLabel;
            var pipeline = context.Auto().Featurizer(train, excludeColumns: new[] { label })
                                .Append(context.Auto().Regression(label, useLgbm: false, useSdca: false, useLbfgs: false));

            experiment.SetDataset(train, 5)
                    .SetRegressionMetric(RegressionMetric.RSquared, label)
                    .SetPipeline(pipeline)
                    .SetTrainingTimeInSeconds(50);

            var result = await experiment.RunAsync();
            result.Metric.Should().BeGreaterThan(0.5);
        }
    }

    class DummyTrialRunner : ITrialRunner
    {
        private readonly int _finishAfterNSeconds;
        private readonly CancellationToken _ct;
        private readonly IChannel _logger;

        public DummyTrialRunner(AutoMLExperiment.AutoMLExperimentSettings automlSettings, int finishAfterNSeconds, IChannel logger)
        {
            _finishAfterNSeconds = finishAfterNSeconds;
            _ct = automlSettings.CancellationToken;
            _logger = logger;
        }

        public TrialResult Run(TrialSettings settings)
        {
            _logger.Info("Update Running Trial");
            Task.Delay(_finishAfterNSeconds * 1000).Wait(_ct);
            _ct.ThrowIfCancellationRequested();
            _logger.Info("Update Completed Trial");
            return new TrialResult
            {
                TrialSettings = settings,
                DurationInMilliseconds = _finishAfterNSeconds * 1000,
                Metric = 1.000 + 0.01 * settings.TrialId,
            };
        }
    }
}