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
using Microsoft.ML.Data;
using Microsoft.ML;

namespace Yelp_restorani
{
    public class SentimentData
    {
        [LoadColumn(0)]
        public string SentimentText;
        [LoadColumn(1), ColumnName("Label")]
        public bool Sentiment;
    }

    public class SentimentPrediction : SentimentData
    {
        [ColumnName("PredictedLabel")]
        public bool Prediction { get; set; }
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

                int totalReviews = 0;
                int positiveReviews = 0;
                int negativeReviews = 0;

                foreach (JObject review in reviews)
                {
                    string userName = review["user"]["name"].ToString();
                    string userReview = review["text"].ToString();
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
            location = Console.ReadLine()!;

            businessObservable.GetBusinesses(location);
            Console.ReadLine();

            subscription1.Dispose();
            subscription2.Dispose();
            subscription3.Dispose();
        }
    }
}