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

# Installation

No installation for this first version. Must be a portable .exe.