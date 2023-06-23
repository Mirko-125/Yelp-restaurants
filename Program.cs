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
    }
    public class BusinessObserver : IObserver<Business>
    {
        private readonly string name;
        public BusinessObserver(string name)
        {
            this.name = name;
        }
        public void OnNext(Business business)
        {
            Console.WriteLine("Presli smo na sledeci biznis");
        }
        public void OnError(Exception error)
        {
            Console.WriteLine(error.Message);
        }
        public void OnCompleted()
        {
            Console.WriteLine($"{name}: Uspesno zavrsen");
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
        public async void GetBusinesses(string location)
        {
            string apiKey = "wIiAX8kQc7QKCa0sUC0h-SaSnw-TqCHfu3dVQiG4tVvLzlGcloY5bPb_X5KZ98_2KLAB9gRcw1rlUVCFMrGo8u2CQc6sX3Iu6Vv0TYUZaIVZueVQPu3qVSISl_GOZHYx"; 
            string apiUrl = $"https://api.yelp.com/v3/businesses/search?location={location}&categories=restaurants";
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            HttpResponseMessage response = await client.GetAsync(apiUrl);
            string content = await response.Content.ReadAsStringAsync();

            JObject jsonResponse = JObject.Parse(content);
            JArray businesses = (JArray)jsonResponse["businesses"];

            foreach (JObject business in businesses)
            {
                string businessId = business["id"].ToString();
                string businessName = business["name"].ToString();
                Console.WriteLine($"{businessName} (ID: {businessId})");

                HttpResponseMessage reviewResponse = await client.GetAsync($"https://api.yelp.com/v3/businesses/{businessId}/reviews");
                string reviewContent = await reviewResponse.Content.ReadAsStringAsync();

                JObject reviewJsonResponse = JObject.Parse(reviewContent);
                JArray reviews = (JArray)reviewJsonResponse["reviews"];

                // int totalReviews = 0;
                // int positiveReviews = 0;
                // int negativeReviews = 0;

                foreach (JObject review in reviews)
                {
                    string userName = review["user"]["name"].ToString();
                    string userReview = review["text"].ToString();

                    // ML training

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

                            rule.Title = "🏁 Training Complete, Evaluating Accuracy.";
                            console.Refresh();

                            // evaluate the accuracy of our model
                            var metrics = ctx.BinaryClassification.Evaluate(predictions);

                            var table = new Table()
                                .MinimalBorder()
                                .Title("💯 Model Accuracy");
                            table.AddColumns("Accuracy", "Auc", "F1Score");
                            table.AddRow($"{metrics.Accuracy:P2}", $"{metrics.AreaUnderRocCurve:P2}", $"{metrics.F1Score:P2}");

                            console.UpdateTarget(table);
                            console.Refresh();
                        });

                    // ?
                    while (true)
                    {
                        var text = AnsiConsole.Ask<string>("What's your [green]review text[/]?");
                        var engine = ctx.Model.CreatePredictionEngine<SentimentData, SentimentPrediction>(model);

                        var input = new SentimentData { Text = text };
                        var result = engine.Predict(input);
                        var style = result.Prediction
                            ? (color: "green", emoji: "👍")
                            : (color: "red", emoji: "👎");

                        Console.MarkupLine($"{style.emoji} [{style.color}]\"{text}\" ({result.Probability:P00})[/] ");
                    }

                    // save to disk
                    ctx.Model.Save(model, dataView.Schema, "model.zip");


                    // load from disk
                    ctx.Model.Load("model.zip", out var schema);

                    /*
                    var mlContext = new MLContext();
                    var modelPath = "./";
                    var model = mlContext.Model.Load(modelPath, out var modelSchema);

                    var predictionEngine = mlContext.Model.CreatePredictionEngine<SentimentData, SentimentPrediction>(model);

                    var sentimentData = new SentimentData { SentimentText = userReview };
                    var sentimentPrediction = predictionEngine.Predict(sentimentData);
                    
                    if (sentimentPrediction.Prediction)
                    {
                        positiveReviews++;
                    }
                    else
                    {
                        negativeReviews++;
                    }
                    totalReviews++;
                    */
                    //Console.WriteLine($"- {userName}: {userReview} (Positive: {sentimentPrediction.Prediction})");
                    Console.WriteLine($"- {userName}: {userReview}");
                }
                /*
                float positivePercentage = (float)positiveReviews / totalReviews * 100;
                float negativePercentage = (float)negativeReviews / totalReviews * 100;

                Console.WriteLine($"Positive Reviews: {positivePercentage}%");
                Console.WriteLine($"Negative Reviews: {negativePercentage}%");
                */
            }
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

            businessObservable.GetBusinesses(location);
            System.Console.ReadLine();

            subscription1.Dispose();
            subscription2.Dispose();
            subscription3.Dispose();
        }
    }
}