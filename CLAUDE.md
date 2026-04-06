# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A C# console application that generates random compliments at random intervals (1-10 minutes). The primary goal is learning OpenTelemetry instrumentation — the app is a vehicle for exploring OTel concepts.

## Key Requirements

- **Console app** with no installation — portable .exe
- Generates compliments automatically while running; no user prompt to trigger them
- User can like/dislike each compliment before the next one arrives; unanswered evaluations are skipped
- No duplicate compliments within a day
- End-of-day report: total generated, liked, disliked
- **OTel "wizard" sidebar**: when telemetry events occur, the app should explain what was emitted and how to query it

## Tech Stack

- C# / .NET (console application)
- OpenTelemetry SDK for .NET — traces, metrics, and logs
- Assume the user is a beginner with OpenTelemetry; explanations and teaching moments are part of the deliverable

## Build & Run (once code exists)

```bash
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Architecture Notes

This repo has no code yet — only a README describing the planned application. When building:

1. Instrument with OpenTelemetry from the start (traces, metrics, logs)
2. Use an OTel exporter that works without external infra for learning (e.g., console exporter or OTLP to a local collector)
3. Keep the teaching/wizard output visually distinct from the compliment output
