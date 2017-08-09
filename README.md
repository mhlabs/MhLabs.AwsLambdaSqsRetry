# MhLabs.AwsLambdaSqsRetry

Abstract class providing two Lambda functions. One which can have any event source trigger and one that is typically triggered on a schedule.

Example usage that consumes an SNS queue and handles failures using an SQS dead letter queue:

## Lambda code:
```
  public class SnsProcessor : MessageProcessor<SNSEvent, List<Product>>
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
