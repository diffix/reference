import random

# Measures how frequently different counts are suppressed by LCF
# given the following parameters. Prints a markdown table with the
# results.

lower = 1.5
mean_sd = [ 
            [3,0.5],
            [3.5,0.5],
            [4,0.5],[4,0.7],[4,1.0],
            [5,0.5],[5,1.0],[5,1.5],
            [6,0.5],[6,1.0],[6,1.5], [6,2.0],
            [7,0.5],[7,1.0],[7,1.5], [7,2.0]
        ]
counts = [1,2,3,4,5,6,7,8,9]
trials = 100000

def do_suppress(count,lower,upper,mean,sd):
    threshold = random.gauss(mean,sd)
    threshold = max(threshold,lower)
    threshold = min(threshold,upper)
    if count < threshold:
        return True
    else:
        return False

results = [{} for _ in range(len(mean_sd))]
for i in range(len(mean_sd)):
    mean = mean_sd[i][0]
    sd = mean_sd[i][1]
    upper = mean + (mean - lower)
    for count in counts:
        results[i][count] = 0
        for _ in range(trials):
            suppress = do_suppress(count,lower,upper,mean,sd)
            if not suppress:
                results[i][count] += 1

print('''
Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed) (`lower=1.5`):
        ''')
# Make results markdown table
print("Config    ",end='')
for count in counts:
    print(f"|  {count}     ",end='')
print('')
print("  ---     ",end='')
for count in counts:
    print(f"|  ---   ",end='')
print('')
for i in range(len(mean_sd)):
    mean = mean_sd[i][0]
    sd = mean_sd[i][1]
    print(f"({mean:1.1f},{sd}) ",end='')
    for count in counts:
        frac = results[i][count] / trials
        print(f"| {frac:0.4f} ",end='')
    print('')

print('''
Suppose that an attacker knows that there are either 1 or 2 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things:
1. The probability that there is in fact 1 AID given that the bucket was suppressed.
2. The probability that there are in fact 2 AIDs given that the bucket was not suppressed.
3. The likelihood that the attacker will learn with certainty that there are 2 AIDs for any given bucket.
        ''')

print("Config    | Prob 1 AID (suppressed) | Prob 2 AIDs (reported) | Prob reported")
print(" ---      | ---                     | ---                    | ---")
for i in range(len(mean_sd)):
    mean = mean_sd[i][0]
    sd = mean_sd[i][1]
    upper = mean + (mean - lower)
    rightWhenSuppressed = 0
    numSuppressed = 0
    rightWhenReported = 0
    numReported = 0
    for _ in range(trials):
        count = random.randrange(1,3)
        suppress = do_suppress(count,lower,upper,mean,sd)
        if suppress:
            numSuppressed += 1
            # Guess count = 1
            if count == 1:
                rightWhenSuppressed += 1
        else:
            numReported += 1
            # Guess count = 2
            if count == 2:
                rightWhenReported += 1
    fracRightWhenSuppressed = rightWhenSuppressed / numSuppressed
    if numReported:
        fracRightWhenReported = rightWhenReported / numReported
        fracReported = int(trials / numReported)
    else:
        fracRightWhenReported = 1
        fracReported = str(f"{trials}++")
    print(f"({mean:1.1f},{sd}) ",end='')
    print(f"| {fracRightWhenSuppressed:0.3f}                   ",end='')
    print(f"| {fracRightWhenReported:0.2f}                   ",end='')
    print(f"| 1/{fracReported}")


print('''
Suppose that an attacker knows that there are either 2 or 3 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things:
1. The probability that there are in fact 2 AIDs given that the bucket was suppressed.
2. The probability that there are in fact 3 AIDs given that the bucket was reported.
3. The probability that the bucket was reported
        ''')

print("Config    | Prob 2 AIDs (suppressed) | Prob 3 AIDs (reported) | Prob reported")
print(" ---      | ---                      | ---                    | ---")
for i in range(len(mean_sd)):
    mean = mean_sd[i][0]
    sd = mean_sd[i][1]
    upper = mean + (mean - lower)
    rightWhenSuppressed = 0
    numSuppressed = 0
    rightWhenReported = 0
    numReported = 0
    for _ in range(trials):
        count = random.randrange(2,4)
        suppress = do_suppress(count,lower,upper,mean,sd)
        if suppress:
            numSuppressed += 1
            # Guess count = 1
            if count == 2:
                rightWhenSuppressed += 1
        else:
            numReported += 1
            # Guess count = 2
            if count == 3:
                rightWhenReported += 1
    fracRightWhenSuppressed = rightWhenSuppressed / numSuppressed
    if numReported:
        fracRightWhenReported = rightWhenReported / numReported
        fracReported = int(trials / numReported)
    else:
        fracRightWhenReported = 1
        fracReported = str(f"{trials}++")
    print(f"({mean:1.1f},{sd}) ",end='')
    print(f"| {fracRightWhenSuppressed:0.3f}                    ",end='')
    print(f"| {fracRightWhenReported:0.2f}                   ",end='')
    print(f"| 1/{fracReported}")

