# MhLabs.AwsLambdaSqsRetry

For cases where we want to have multiple subscribers to events and where Kinesis doesn't make sense due to the fixed shard configuration. For sporadic messages / infrequent message bursts, the procing model for SNS is much cheaper and it scales more flexibly. However, Contrary to Kinesis, SNS doesn't provide any retry logic apart from the 3 retries that's default to AWS Lambda. If the third Lambda invokation attemt falis, the message gets lost.

Ideally we'd want an SQS queue to subscribe to an SNS topic and trigger the Lambda function, but so far AWS hasn't provided support for SQS as a Lambda event source.

This lets us consume from an SNS topic and solve the retry issue by specifying an SQS queue as Dead Letter Queue for the Lambda. Any failed Lambda events gets sent to the DLQ. The DLQ can be configured to have a delivery delay, so say we have a DynamoDB table with a low write capacity, but with an auto scaling policy enabled. If a burst of messages comes through, they will start getting rejected due to the sudden high write capacity. All these messages are put on the SQS DLQ with a 5 minute delivery delay.

The SQS DQL is polled every minute by an additional Lambda, which effectively is invoking the same method as the SNS consumer. The five minute delay gives time for the auto scaling to kick in and once the write capacity has been increased the messages in the DLQ are likely to be accepted by DynamoDB. If they fail again, they will go back to SQS where they will be retried. The default retention time in SQS is 4 days.


## Lambda code:
```
  public class SnsProcessor : MessageProcessorBase<SNSEvent, List<Product>>
    {
        private readonly ProductRepository _repo;

        public SnsProcessor() 
        {
            _handler = new ProductRepository();
        }

        // Method triggered by Lambda
        protected override async Task HandleEvent(List<Product> products, ILambdaContext context)
        {
            var records = new List<Product>();
            foreach (var product in products)
            {
                if (product != null) {
                   records.Add(product);
                }
            }
            await _repo.Add(records);
        }

        protected override List<Products> ExtractEventBody(SNSEvent ev)
        {
            return JsonConvert.DeserializeObject<List<Product>>(ev.Records.FirstOrDefault()?.Sns?.Message);
        }
    }
```

## Serverless.template
```
{
    "AWSTemplateFormatVersion": "2010-09-09",
    "Transform": "AWS::Serverless-2016-10-31",
    "Resources": {
        "SnsConsumer": {
            "Type": "AWS::Serverless::Function",
            "Properties": {
                "Handler": "product_service::product_service.SnsProcessor::Process",
                "Runtime": "dotnetcore1.0",
                "CodeUri": "",
                "MemorySize": 128,
                "Timeout": 30,
                "Role": null,
                "Policies": [
                    "AWSLambdaFullAccess",
                    "AmazonDynamoDBFullAccess"
                ],
                "DeadLetterQueue": {
                    "Type": "SQS",
                    "TargetArn": {
                        "Fn::GetAtt": [
                            "DeadLetterQueue",
                            "Arn"
                        ]
                    }
                },
                "Events": {
                    "PutResource": {
                        "Type": "SNS",
                        "Properties": {
                            "Topic": {
                                "Fn::ImportValue": "product-TopicArn"
                            }
                        }
                    }
                }
            }
        },
        "ProcessRetries": {
            "Type": "AWS::Serverless::Function",
            "Properties": {
                "Handler": "product_service::product_service.SnsProcessor::RetryBatch",
                "Runtime": "dotnetcore1.0",
                "CodeUri": "",
                "MemorySize": 128,
                "Timeout": 30,
                "Role": null,
                "Policies": [
                    "AWSLambdaFullAccess",
                    "AmazonDynamoDBFullAccess",
                    "AmazonSQSFullAccess"
                ],
                "Environment": {
                    "Variables": {
                        "RetryQueueUrl": {
                            "Ref": "DeadLetterQueue"
                        }
                    }
                },
                "Events": {
                    "PutResource": {
                        "Type": "Schedule",
                        "Properties": {
                            "Schedule": "rate(1 minute)"
                        }
                    }
                }
            }
        },       
        "DeadLetterQueue": {
            "Type": "AWS::SQS::Queue",
            "Properties": {
                "DelaySeconds": 300
            }
        }
    },
    "Outputs": {}
}
```

## Flow:
![alt text](https://raw.githubusercontent.com/mhlabs/MhLabs.AwsLambdaSqsRetry/master/sns-sqs-flow.png "Data Flow")
