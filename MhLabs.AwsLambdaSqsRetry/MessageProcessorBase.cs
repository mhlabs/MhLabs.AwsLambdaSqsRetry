using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;

namespace MhLabs.AwsLambdaSqsRetry
{
    public abstract class MessageProcessor<TEventType, TRawData>
    {
        protected abstract Task HandleEvent(TRawData raw, ILambdaContext context);

        private readonly IAmazonSQS _sqsClient;

        protected MessageProcessor()
        {
            _sqsClient = new AmazonSQSClient(RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")));
        }

        [LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
        public async Task RetryBatch(ILambdaContext context)
        {

            var request = new ReceiveMessageRequest
            {
                QueueUrl = Environment.GetEnvironmentVariable("RetryQueueUrl"),
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 1 //TODO - probably shouldn't do long polling here
            };
            var hasMessages = false;
            do
            {
                var queueResponse = await _sqsClient.ReceiveMessageAsync(request);
                hasMessages = queueResponse.Messages.Any();
                var deleteBatch = new List<DeleteMessageBatchRequestEntry>();
                var count = 0;
                foreach (var message in queueResponse.Messages)
                {
                    Console.WriteLine(message.Body);
                    var obj = JsonConvert.DeserializeObject<TEventType>(message.Body);
                    await Process(obj, context);
                    deleteBatch.Add(new DeleteMessageBatchRequestEntry(count++.ToString(), message.ReceiptHandle));
                }
                if (deleteBatch.Any())
                {
                    await _sqsClient.DeleteMessageBatchAsync(new DeleteMessageBatchRequest
                    {
                        QueueUrl = request.QueueUrl,
                        Entries = deleteBatch
                    });
                }
            } while (hasMessages);
        }

        protected abstract TRawData ExtractEventBody(TEventType ev);

        [LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
        public async Task Process(TEventType ev, ILambdaContext context)
        {
            LambdaLogger.Log("Processing started");
            var rawData = ExtractEventBody(ev);
            await HandleEvent(rawData, context);

            LambdaLogger.Log("Processing complete.");
        }
    }
}
