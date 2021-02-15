import random
import pandas as pd

# Measures how frequently different counts are suppressed by LCF
# given the following parameters. Prints a markdown table with the
# results.

def do_suppress(count,always_suppress_upper_bound,mean,sd):
    upper = mean + (mean - always_suppress_upper_bound)
    threshold = random.gauss(mean,sd)
    threshold = max(threshold,always_suppress_upper_bound)
    threshold = min(threshold,upper)
    if count <= threshold:
        return True
    else:
        return False

def do_basic_probs(always_suppress_upper_bound,mean_sd,counts,trials):
    results = {}
    results['(mean,sd)'] = []
    for count in counts:
        results[count] = [0 for _ in range(len(mean_sd))]
    for i in range(len(mean_sd)):
        mean = mean_sd[i][0]
        sd = mean_sd[i][1]
        results['(mean,sd)'].append(f"({mean:1.1f},{sd})")
        for count in counts:
            for _ in range(trials):
                # Here we suppress without the hard lower limit, just to see
                # how often the hard limit would be invoked
                suppress = do_suppress(count,0,mean,sd)
                if not suppress:
                    results[count][i] += 1
    for count in counts:
        for i in range(len(results[count])):
            frac = results[count][i] / trials
            results[count][i] = f"{frac:0.5f}"
    
    df = pd.DataFrame.from_dict(results)

    print(f'''
Given the count of distinct AIDs in a bucket, what is the probability that the bucket will be reported (not suppressed). In producing these numbers, we set `always_suppress_upper_bound=0` so that we can see how often the lower limit would have been hit. In practice we would never set `always_suppress_upper_bound=0`.
        ''')
    print(df.to_markdown(index=False))


def do_conditional_probs(always_suppress_upper_bound,mean_sd,counts,trials):
    res = {}
    res['(mean,sd)'] = []
    res['N'] = []
    res['Prob N AIDs (sup)'] = []
    res['Prob N+1 AIDs (rep)'] = []
    res['Prob reported'] = []
    for i in range(len(mean_sd)):
        mean = mean_sd[i][0]
        sd = mean_sd[i][1]
        for n in range(1,int(mean)):
            res['(mean,sd)'].append(f"({mean:1.1f},{sd})")
            res['N'].append(f"{n}")
            rightWhenSuppressed = 0
            numSuppressed = 0
            rightWhenReported = 0
            numReported = 0
            for _ in range(trials):
                count = random.randrange(n,n+2)
                suppress = do_suppress(count,always_suppress_upper_bound,mean,sd)
                if suppress:
                    numSuppressed += 1
                    # Guess count = 1
                    if count == n:
                        rightWhenSuppressed += 1
                else:
                    numReported += 1
                    # Guess count = 2
                    if count == n+1:
                        rightWhenReported += 1
            fracRightWhenSuppressed = rightWhenSuppressed / numSuppressed
            res['Prob N AIDs (sup)'].append(f"{fracRightWhenSuppressed:0.3f}")
            if numReported:
                fracRightWhenReported = rightWhenReported / numReported
                fracReported = int(trials / numReported)
                res['Prob N+1 AIDs (rep)'].append(f"{fracRightWhenReported:0.2f}")
                res['Prob reported'].append(f"1/{fracReported}")
            else:
                res['Prob N+1 AIDs (rep)'].append('1')
                res['Prob reported'].append(f"1/{trials}++")
        res['(mean,sd)'].append(' ')
        res['N'].append(' ')
        res['Prob N AIDs (sup)'].append(' ')
        res['Prob N+1 AIDs (rep)'].append(' ')
        res['Prob reported'].append(' ')
    
    print(f'''
Suppose that an attacker knows that there are either N or N+1 AIDs in a bucket.  Suppose that the probability of either outcome is 50%. The following table shows three things (always_suppress_upper_bound={always_suppress_upper_bound}):
1. The probability that there is in fact N AIDs given that the bucket is suppressed.
2. The probability that there are in fact N+1 AIDs given that the bucket is reported.
3. The likelihood that the bucket is reported.
        ''')
    df = pd.DataFrame.from_dict(res)
    print(df.to_markdown(index=False))

# -------------------------------------------------------------

# Let's measure the following mean values (plus always_suppress_upper_bound)
base_means = [2,2.5,3,4,6,8]
# Let's measure a factor of this many SDs between always_suppress_upper_bound and mean
sd_factors = [2,3,4,5]
# Let's measure these values for always_suppress_upper_bound
lowers = [1.5,2.5]
# Let's look at buckets with this many distinct AIDs (plus lower)
base_counts = [0,1,2,3,4,5,6,7,8]
trials = 1000000

for always_suppress_upper_bound in lowers:
    counts = []
    for base_count in base_counts:
        counts.append(base_count + always_suppress_upper_bound)
    means = []
    for base_mean in base_means:
        means.append(base_mean + always_suppress_upper_bound)
    mean_sd = []
    for mean in means:
        for sd_factor in sd_factors:
            sd = round((mean - always_suppress_upper_bound) / sd_factor,1)
            mean_sd.append([mean,sd])
    do_basic_probs(always_suppress_upper_bound,mean_sd,counts,trials)
    do_conditional_probs(always_suppress_upper_bound,mean_sd,counts,trials)
