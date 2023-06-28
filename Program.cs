// Koristeći principe Reaktivnog programiranja i Yelp API ,
// implementirati aplikaciju za analizu komentara za restorane na prosleđenoj lokaciji (location parametar).
// Za prikupljene komentare implementirati Sentiment analizu koristeći SentimentAnalysis.NET ili ML.NET biblioteke. 
// Prikazati dobijene rezultate i procenat pozitivnih i negativnih komentara za prikupljene restorane.

using Yelp.Api;
using Yelp.Api.Models;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Newtonsoft.Json.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Spectre.Console;
using Console = Spectre.Console.AnsiConsole;
using System.Reactive.Concurrency;
using System.Reactive;
using System;

namespace Yelp_restorani
{
    class SentimentData
    {
        [LoadColumn(0)] public string? Text;
        [LoadColumn(1), ColumnName("Label")] public bool Sentiment;
    }

    class SentimentPrediction : SentimentData
    {
        [ColumnName("PredictedLabel")] public bool Prediction { get; set; }
        public float Probability { get; set; }
        public float Score { get; set; }
    }

    public class Business
    {
        public string? Name { get; set; }
        public Business(string? name)
        {
            Name = name;
        }
    }
    public class BusinessObserver : IObserver<Business>
    {
        private readonly string name;
        private int positiveCount;
        private int negativeCount;

        public BusinessObserver(string name)
        {
            this.name = name;
            positiveCount = 0;
            negativeCount = 0;
        }

        public void OnNext(Business business)
        {
            Console.WriteLine("Passed on next business, now completed: " + business.Name+".");
        }

        public void OnError(Exception error)
        {
            Console.WriteLine(error.Message);
        }

        public void OnCompleted()
        {
            Console.WriteLine($"{name}: Complete!");
            PrintReviewStatistics();
        }

        public void AddReviewSentiment(bool isPositive)
        {
            if (isPositive)
            {
                positiveCount++;
            }
            else
            {
                negativeCount++;
            }
        }

        private void PrintReviewStatistics()
        {
            int totalCount = positiveCount + negativeCount;
            double positivePercentage = (double)positiveCount / totalCount * 100;
            double negativePercentage = (double)negativeCount / totalCount * 100;

            Console.WriteLine($"Positive reviews: {positiveCount} ({positivePercentage:F2}%)");
            Console.WriteLine($"Negative reviews: {negativeCount} ({negativePercentage:F2}%)");
        }
    }
    public class BusinessObservable : IObservable<Business>
    {
        private readonly Subject<Business> subject;
        public BusinessObservable()
        {
            subject = new Subject<Business>();
        }
        public IDisposable Subscribe(IObserver<Business> observer)
        {
            return subject.Subscribe(observer);
        }
        public async void GetBusinesses(string location, IScheduler scheduler, BusinessObserver observer)
        {
            #region ML.NET Training
            // ML model training
            var ctx = new MLContext();

            // load data
            var dataView = ctx.Data
                .LoadFromTextFile<SentimentData>("yelp_labelled.txt");

            // split data into testing set
            var splitDataView = ctx.Data
                .TrainTestSplit(dataView, testFraction: 0.2);

            // Build model
            var estimator = ctx.Transforms.Text
                .FeaturizeText(
                    outputColumnName: "Features",
                    inputColumnName: nameof(SentimentData.Text)
                ).Append(ctx.BinaryClassification.Trainers.SdcaLogisticRegression(featureColumnName: "Features"));

            // Train model
            ITransformer model = default!;

            var rule = new Rule("Create and Train Model");
            Console
                .Live(rule)
                .Start(console =>
                {
                    // training happens here
                    model = estimator.Fit(splitDataView.TrainSet);
                    var predictions = model.Transform(splitDataView.TestSet);

                    rule.Title = "Training Complete, Evaluating Accuracy.";
                    console.Refresh();

                    // evaluate the accuracy of our model
                    var metrics = ctx.BinaryClassification.Evaluate(predictions);

                    var table = new Table()
                        .MinimalBorder()
                        .Title("Model Accuracy");
                    table.AddColumns("Accuracy", "Auc", "F1Score");
                    table.AddRow($"{metrics.Accuracy:P2}", $"{metrics.AreaUnderRocCurve:P2}", $"{metrics.F1Score:P2}");

                    console.UpdateTarget(table);
                    console.Refresh();
                });

            // save to disk
            ctx.Model.Save(model, dataView.Schema, "model.zip");

            // load from disk
            ctx.Model.Load("model.zip", out var schema);
            #endregion

            await Observable.Start(async () =>
            {
                try
                {
                    string apiKey = "wIiAX8kQc7QKCa0sUC0h-SaSnw-TqCHfu3dVQiG4tVvLzlGcloY5bPb_X5KZ98_2KLAB9gRcw1rlUVCFMrGo8u2CQc6sX3Iu6Vv0TYUZaIVZueVQPu3qVSISl_GOZHYx";
                    string apiUrl = $"https://api.yelp.com/v3/businesses/search?location={location}&categories=restaurants";
                    using HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    string content = await response.Content.ReadAsStringAsync();

                    JObject jsonResponse = JObject.Parse(content);
                    JArray businesses = (JArray)jsonResponse["businesses"]!;

                    foreach (JObject business in businesses)
                    {
                        string businessId = business["id"]!.ToString();
                        string businessName = business["name"]!.ToString();

                        Business business1 = new Business(businessName);

                        Console.WriteLine($"{businessName} (ID: {businessId})");

                        HttpResponseMessage reviewResponse = await client.GetAsync($"https://api.yelp.com/v3/businesses/{businessId}/reviews");
                        string reviewContent = await reviewResponse.Content.ReadAsStringAsync();

                        JObject reviewJsonResponse = JObject.Parse(reviewContent);
                        JArray reviews = (JArray)reviewJsonResponse["reviews"]!;

                        foreach (JObject review in reviews)
                        {
                            string userName = review["user"]!["name"]!.ToString();
                            string userReview = review["text"]!.ToString();

                            var engine = ctx.Model.CreatePredictionEngine<SentimentData, SentimentPrediction>(model);

                            var input = new SentimentData { Text = userReview };
                            var result = engine.Predict(input);
                            var style = result.Prediction ? (color: "gray", emoji: "👍") : (color: "gray", emoji: "👎");

                            Console.WriteLine($"- {userName}: {userReview}");
                            Console.MarkupLine($"[{style.color}] ({result.Probability:P00})[/] ");
                            
                            Console.WriteLine();

                            bool isPositive = result.Prediction;
                            observer.AddReviewSentiment(isPositive);
                        }
                        observer.OnNext(business1);       
                    }
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            },scheduler);

        }
    }
    internal class Program
    {
        public static void Main()
        {
            var businessObservable = new BusinessObservable();

            var observer1 = new BusinessObserver("Observer 1");
            var observer2 = new BusinessObserver("Observer 2");
            var observer3 = new BusinessObserver("Observer 3");

            var filtriraniStream = businessObservable;

            var subscription1 = filtriraniStream.Subscribe(observer1);
            var subscription2 = filtriraniStream.Subscribe(observer2);
            var subscription3 = filtriraniStream.Subscribe(observer3);

            string location;
            Console.WriteLine("Enter your wanted location:");
            location = System.Console.ReadLine()!;

            IScheduler scheduler = NewThreadScheduler.Default;

            businessObservable.GetBusinesses(location,scheduler,observer1);
            System.Console.ReadLine();

            subscription1.Dispose();
            subscription2.Dispose();
            subscription3.Dispose();
        }
    }
}