# Background Service in .NET

Every class that wants to have the background service functionality in NET should inherit from the `BackgroundService` class.

## Implementation details

You can find useful information on how its implemented backgroundservice looking into github:
- [BackgroundService implementation in .NET](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs)

## Hosted service

When you need to run a background service under .NET.
We can avoid the boilerplate code simply just consuming the class BackgroundService.

### Use Cases
- Pooling for data from an external service.
- Responding to external message or events.
- Performing data-intensive work, outside of the request lifecycle.

### Coordination between requests and hosted services

You can connect a producer vs consumer in .net core using channels, then the user can by through a request something and the background service can consume it asynchronously.

Usually if you are going to create a channel, you should create as a singleton (single instance).

# Worker Service

While hosted service is a classe where it does the background work, a worker service is a template provided by .NET to create long-running background services with minimal setup.

Another interesting thing is that we can deploy independently from the main application.

## Common works
- Processing messages/events from a queue, service bus or event stream.
- Reacting to file changes in a object/file store.
- Aggregating data from a data store.
- Enriching data in data ingestion pipelines.
- Formatting and cleansing of AI/ML datasets.

# Host

Manages application lifetime.

Provides components, such as dependency injection, logging and configuration.

Turns a console application into a long-running service.

Starts and stops hosted services.

** HOST since net core 3 **: Since net core 3, web host and generic host have been unified under the generic host model.

And is that why we are encouraged to use webapplication builder since net 6 that under the hood it has generic host running.
And in .NET 7 we had the introduction of hostapplicationbuilder where is far more flexible than webapplicationbuilder.