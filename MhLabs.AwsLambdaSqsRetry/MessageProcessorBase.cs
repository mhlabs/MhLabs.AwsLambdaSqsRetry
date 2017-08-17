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
    public abstract class MessageProcessorBase<TEventType, TRawData>
    {
        protected abstract Task HandleEvent(TRawData raw, ILambdaContext context);

        private readonly IAmazonSQS _sqsClient;

        protected MessageProcessorBase()
        {
            _sqsClient = new AmazonSQSClient(RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")));
        }

        [LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
        public async Task RetryBatch(ILambdaContext context)
        {

            var request = new ReceiveMessageRequest
            {
                QueueUrl = Environment.GetEnvironmentVariable("RetryQueueUrl"),
                MaxNumberOfMessages = 10
            };

            bool hasMessages;
            do
            {
                var queueResponse = await _sqsClient.ReceiveMessageAsync(request);
                hasMessages = queueResponse.Messages.Any();
                foreach (var message in queueResponse.Messages)
                {
                    Console.WriteLine(message.Body);
                    var obj = JsonConvert.DeserializeObject<TEventType>(message.Body);
                    await Process(obj, context);
                    await _sqsClient.DeleteMessageAsync(new DeleteMessageRequest(request.QueueUrl, message.ReceiptHandle));
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
