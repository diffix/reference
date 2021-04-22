----------
# Histograms

For histograms (i.e. bucket functions) I think we should still enforce snapping like we currently do, with the exception that we can allow finer granularity and more options (powers of 2, money style, etc). This way we can avoid the inaccuracies inherent in the solution for inequalities given above.

----------
# Inequalities

I believe we can do away with the range and snapping requirements, at least from the point of view of the analyst. Under the hood we'll still have snapping. For the sake of the following discussion, let's assume that we snap ranges and offsets by powers of 2 (1/4, 1/2, 1, 2, 4...), and that we allow shifting of an offset by 1/2 or the range. Note that the choice of snapping increments is hidden from the analyst, so these don't have to be user friendly per se.

Basic idea is this. When an inequality is specified, we want to find a snapped range inside the inequality that includes a noisy-threshold number of distinct users (say between 2 and 4 distinct users). The box can align with the edge of the inequality (i.e. the box for `col <= 10` can have 10 as it's right edge). The reason the box is inside the inequality is because the rows inside the inequality will be included in the query execution, and therefore are more likely to be accessible to our logic.

The drawing here illustrates that. Here the analyst has specified a `<=` condition (the red line, which we'll call the edge). The x's represent the data points, and for the sake of simplicity we'll assume one data point per distinct AID value. The drawing shows two scenarios, one where the data points are relatively more densely populated, and one where they are relatively more sparsely populated.

![](https://paper-attachments.dropbox.com/s_832D3C952962442CDA33E4F625E4143AA62099E867229D6068A42F99D3011C9E_1599545192241_image.png)


The blue boxes represent snapped ranges within the edge. The box for the dense scenario has 5 distinct AID values, and the box for the sparse scenario has 3 distinct AID values. Data points inside the boxes are included in the answer, but data points between the boxes and the edge are excluded (marked in red). The noise layers would be seeded by the size and position of the box.

A critical point is that if the edge is moved left or right slightly, this would not change the chosen boxes. This makes it hard to do an averaging attack on the condition's noise layer.

As the edge is moved further, it would get to the point where a new box would be chosen, possibly bigger or smaller than the previous box, probably but not necessarily including or excluding different AID values, but in any event the choice of the new box would not be triggered by crossing a given data point, but rather by moving far enough that a new box is a better fit. This is not to say that the box isn't data driven, it is. However, typically the choice of box is based on the contributions of multiple AID values, not a single AID value.

If the edge matches a snapped value, then it may well be the case that no data points are excluded. If the analyst specifies a range, and the range itself matches a snapped range that is allowed for histograms, then we can use the traditional seeding approach which would in turn match the seeds of histograms.

For instance, if the range is `WHERE age BETWEEN 10 and 20`, then we can seed the same as a corresponding bucket generated with `SELECT bucket(age BY 10)`.

There are still some details to be worked out regarding which box to use when there are several choices. Probably we want to minimize the number of dropped data points. This in turn can (normally) best be done when the box itself is small, because this allows for finer-grained offsets.

Sebastian mentions this case:


    SELECT age, count(*)
    FROM users
    WHERE age > 10

which could in principle display a value for `age = 10`. Sebastian points out that this is probably a non-issue since the `age=10` bucket would almost certainly be suppressed. Nevertheless we might need an explicit check to prevent it.

For instance we could perhaps ensure the boxes are always strictly greater or equal to the range. I.e. `age > 10` means the lower bound of the range has to be greater than 10 too... In that case the value for `age = 10` would not be part of the result set at all. If the dataset allows it, we might get values from 11 onwards (assuming the range `[11,11]` passes the low count filter).

In the case where there are no values to the right (left) of a less-than (greater-than) condition, we don't need the box to encompass the value. Rather we can treat it as though the analyst edge were identical to the right-most (left-most) data point. This prevents an averaging attack where the analyst just selects edges further and further to the right (left) of the max (min) value.

For instance, suppose the condition is `WHERE age < 1001`. Suppose the highest age in the system is 102. Then the bounding box might well be `[98,100]` (if there are enough users in that box).

No doubt other missing details here, but this is the basic idea and it looks solid to me.

> TODO: Need to code a simulation of this approach and try a variety of attacks

## Unclean inequalities

It occurs to me that the above discussion implicitly assumes that inequalities are clean (i.e. we know the value of the edge. If not clean, though, this begs the question of how the DB knows what to access from an index. They must have some way of knowing what in the index is included and excluded, and if they know this, then we could leverage it to allow unclean inequalities.

For instance, if the condition is `WHERE sqrt(age) < 5`, we would somehow know that this really means `age < 25` (based on knowledge we borrow from Postgres) and proceed accordingly.

> TODO: We need to think about cases where changing an edge from one query to the next can isolate a single user. To the extent that such cases exist, we may need to detect them.

## Time-based attacks ('now()' as inequality)

One of the attacks that we've never addressed is that of repeating a query and detecting the difference between the two queries. If it is somehow known that only one user changed in the query context between the two queries then this can be detected.

If the DB records a timestamp for when rows are added, we can leverage this by implicitly treating each query as having `WHERE ts <= now()` attached to it, and treating this inequality as described above.

From Sebastian:

This is mostly a problem for datasets that continuously evolve. In those instances, we need some way of knowing when a row was inserted or changed. A very hairy solution would be to implement our own "index type" (or other ability to record metadata) which is such that inserts/writes to the database triggers an update. This way we can record metadata when rows are inserted/changed and use that information as a way of determining what data to include and what to exclude.

For datasets that have a known update frequency, we could do something akin to what I described in [this issue](https://github.com/diffix/strategy/issues/7):

Namely, we could:
- have a "dataset version id" and an accompanying noise layer. Hence each new version of a dataset (whether or not the data has changed), will produce a different result
- use a noise layer based on a pre-determined update frequency. I.e. if the dataset is updating once per month, then we could add a noise layer seeded by `bucket(current time by update frequency)`. Of course this value must align with the update frequency! Otherwise, you would get a new noise value both when data has changed and when the update interval changes...

