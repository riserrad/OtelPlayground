# Overview

I am creating this project as a pet project to help me learn more about how OpenTelemetry works.

I will ask Claude to create the code to build a simple console application to generate random compliments to my wife.

# Application

I want to build a console application that will generate random compliments so I can send them to my wife. I don't want to ask the application to generate a compliment. As long as I keep it open, it will generate compliments at random time intervals. These intervals need to be reasonable - i.e., it can't be like after 10 seconds from the previous one. Let's say that the intervals must be between 1 and 10 minutes.

The console must allow me to like or dislike a generated compliment. But if another compliment is generated and I provide no response, it should just skip my evaluation.

At the end of the day, I want the app to generate a report of how many compliments it generated, how many I liked and how many I disliked.

It cannot generate duplicate compliments within a day.

# Tech stack

I want to use C#. Remember: my main goal is to emit telemetry with Open Telemetry and learn how it works.

So if the application can have a side "wizard" telling "look, as this event occurred, I have sent telemetry that you can view from [teach how to query the telemtry]".

Assume I know nothing about OpenTelemetry.

# Interface

Command line.

# How it works

The app uses **OpenTelemetry (OTel)** to emit telemetry — traces, metrics, and logs — every time something meaningful happens (a compliment is shown, the user likes/dislikes it, etc.).

That telemetry is sent using **OTLP (OpenTelemetry Protocol)**, the standard wire format for shipping observability data. Think of OTLP as the "USB-C" of observability: one protocol that works with many backends. You instrument your app once, and can send telemetry to any OTLP-compatible receiver just by changing a URL — no code changes needed.

The project has two apps:

- **ComplimentGenerator** — the main app. Shows compliments, collects feedback, emits telemetry via OTLP.
- **OTelWizard** — a sidecar that receives that telemetry and explains it in plain English, so you can learn what each trace, metric, and log means.

# Running

## Option 1: OTelWizard (learning mode)

Start the wizard sidecar first, then the main app in a second terminal:

```bash
# Terminal 1
cd src/OTelWizard
dotnet run

# Terminal 2
cd src/ComplimentGenerator
dotnet run
```

The wizard will display each piece of telemetry with color-coded explanations as it arrives.

## Option 2: Aspire Dashboard (visual mode)

The [.NET Aspire Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/standalone) gives you a full visual UI to explore traces, metrics, and logs. Requires Docker.

```bash
# Terminal 1 — start the dashboard
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest

# Terminal 2 — start the main app
cd src/ComplimentGenerator
dotnet run
```

Then open http://localhost:18888 in your browser.

Since the ComplimentGenerator exports to `localhost:4317` by default, and the Docker command maps that port to the dashboard's OTLP receiver, it works with zero configuration. You can also change the endpoint with the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.

# Installation

No installation for this first version. Must be a portable .exe.