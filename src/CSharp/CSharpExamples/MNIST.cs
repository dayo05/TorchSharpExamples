// Copyright (c) .NET Foundation and Contributors.  All Rights Reserved.  See LICENSE in the project root for license information.
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;

using TorchSharp;
using TorchSharp.torchvision;

using TorchSharp.Examples;
using TorchSharp.Examples.Utils;

using static TorchSharp.torch;

using static TorchSharp.torch.nn;
using static TorchSharp.torch.nn.functional;

namespace CSharpExamples
{
    /// <summary>
    /// Simple MNIST Convolutional model.
    /// </summary>
    /// <remarks>
    /// There are at least two interesting data sets to use with this example:
    /// 
    /// 1. The classic MNIST set of 60000 images of handwritten digits.
    ///
    ///     It is available at: http://yann.lecun.com/exdb/mnist/
    ///     
    /// 2. The 'fashion-mnist' data set, which has the exact same file names and format as MNIST, but is a harder
    ///    data set to train on. It's just as large as MNIST, and has the same 60/10 split of training and test
    ///    data.
    ///    It is available at: https://github.com/zalandoresearch/fashion-mnist/tree/master/data/fashion
    ///
    /// In each case, there are four .gz files to download. Place them in a folder and then point the '_dataLocation'
    /// constant below at the folder location.
    /// </remarks>
    public class MNIST
    {
        private static int _epochs = 4;
        private static int _trainBatchSize = 64;
        private static int _testBatchSize = 128;

        private readonly static int _logInterval = 100;

        internal static void Run(int epochs, int timeout, string dataset)
        {
            _epochs = epochs;

            if (string.IsNullOrEmpty(dataset)) {
                dataset = "mnist";
            }

            var device = cuda.is_available() ? CUDA : CPU;

            Console.WriteLine();
            Console.WriteLine($"\tRunning MNIST with {dataset} on {device.type.ToString()} for {epochs} epochs, terminating after {TimeSpan.FromSeconds(timeout)}.");
            Console.WriteLine();

            random.manual_seed(1);

            if (device.type == DeviceType.CUDA) {
                _trainBatchSize *= 4;
                _testBatchSize *= 4;
            }

            Console.WriteLine($"\tCreating the model...");

            var model = new TorchSharp.Examples.MNIST.Model("model", device);

            var normImage = transforms.Normalize(new double[] { 0.1307 }, new double[] { 0.3081 }, device: (Device)device);

            Console.WriteLine($"\tPreparing training and test data...");
            Console.WriteLine();

            using (MNISTReader train = new MNISTReader(download: true, is_train: true, batch_size: _trainBatchSize, device: device, shuffle: true, transform: normImage),
                                test = new MNISTReader(download: true, is_train: false, batch_size: _testBatchSize, device: device, transform: normImage)) {

                TrainingLoop(dataset, timeout, device, model, train, test);
            }
        }

        internal static void TrainingLoop(string dataset, int timeout, Device device, Module model, MNISTReader train, MNISTReader test)
        {
            var optimizer = optim.Adam(model.parameters());

            var scheduler = optim.lr_scheduler.StepLR(optimizer, 1, 0.7, last_epoch: 5);

            Stopwatch totalTime = new Stopwatch();
            totalTime.Start();

            for (var epoch = 1; epoch <= _epochs; epoch++) {

                Train(model, optimizer, nll_loss(reduction: Reduction.Mean), device, train, epoch, train.BatchSize, train.Size);
                Test(model, nll_loss(reduction: nn.Reduction.Sum), device, test, test.Size);

                Console.WriteLine($"End-of-epoch memory use: {GC.GetTotalMemory(false)}");

                if (totalTime.Elapsed.TotalSeconds > timeout) break;
            }

            totalTime.Stop();
            Console.WriteLine($"Elapsed time: {totalTime.Elapsed.TotalSeconds:F1} s.");

            Console.WriteLine("Saving model to '{0}'", dataset + ".model.bin");
            model.save(dataset + ".model.bin");
        }

        private static void Train(
            Module model,
            optim.Optimizer optimizer,
            Loss loss,
            Device device,
            IEnumerable<(Tensor, Tensor)> dataLoader,
            int epoch,
            long batchSize,
            long size)
        {
            model.Train();

            int batchId = 1;

            Console.WriteLine($"Epoch: {epoch}...");
            foreach (var (data, target) in dataLoader) {
                optimizer.zero_grad();

                var prediction = model.forward(data);
                var output = loss(prediction, target);

                output.backward();

                optimizer.step();

                if (batchId % _logInterval == 0) {
                    Console.WriteLine($"\rTrain: epoch {epoch} [{batchId * batchSize} / {size}] Loss: {output.ToSingle():F4}");
                }

                batchId++;

                GC.Collect();
            }
        }

        private static void Test(
            Module model,
            Loss loss,
            Device device,
            IEnumerable<(Tensor, Tensor)> dataLoader,
            long size)
        {
            model.Eval();

            double testLoss = 0;
            int correct = 0;

            foreach (var (data, target) in dataLoader) {
                var prediction = model.forward(data);
                var output = loss(prediction, target);
                testLoss += output.ToSingle();

                var pred = prediction.argmax(1);
                correct += pred.eq(target).sum().ToInt32();

                pred.Dispose();

                GC.Collect();
            }

            Console.WriteLine($"Size: {size}, Total: {size}");

            Console.WriteLine($"\rTest set: Average loss {(testLoss / size):F4} | Accuracy {((double)correct / size):P2}");
        }
    }
}
