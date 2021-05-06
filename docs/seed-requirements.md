# General Requirements for Seeding

This document lays out the basic properties that we want seeding to achieve. This in turn can be used to drive the specific mechanisms we use.

## Background

One of the fundamental properties of Diffix is sticky layered noise. At a conceptual level, "sticky" means that the "same query" produces the "same noise". This is to prevent averaging attacks where the same query is simply repeated. "Layered" means that we have distinct noise samples for each condition in the query (where a condition is broadly interpreted to mean "anything that can change what rows contributes to an answer").

A noise value is determined by how the PRNG is seeded. Therefore seeding is a critical aspect of Diffix. We refer to the elements that comprise a seed as "seed material". 

We want seeding to have the following properties:

1. For conditions that select at least one row:
  1. Two conditions that would select the same rows from the table being queried should *almost always* have the same seed material.
  2. Two conditions what would select different rows from the table being queried should *usually* have different seed materials.
2. For conditions that select zero rows:
  1. Two conditions that would select the same rows from any arbitrary table should *almost always* have the same seed material.
  2. Two conditions what would select different rows from any arbitrary table should *usually* have different seed materials.
3. To the extent that the above properties are not satisfied, an attacker should not be able to predict when they will happen.

The notion of "would select the same rows from any given table" needs explanation. 

By "select", I mean the *condition* evaluates as true. (Since there are multiple conditions, one condition may evaluate as true while the set of conditions evaluates as false. We don't care about that: we only care about the specific condition for which we are seeding.)

By "from any given table", I mean that the property should hold not only for the tables being queried, but for any possible table. For example, say we have two conditions, `age=1000` and `age=1001`. It might be that for the specific table we are querying they select the same rows (i.e. no rows at all). However, there is a possible table that happens to have values 1000 and 1001 in the age column, and so these two conditions should have different seed material.

## Examples of the properties
