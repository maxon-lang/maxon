package main

import (
	"fmt"
	"runtime"
	"sync"

	"maxon.dev/bench/concurrency/internal/argv"
)

const defaultCoroutineCount = 100000

func main() {
	coroutineCount := argv.Size(1, defaultCoroutineCount)
	results := make([]int, coroutineCount)

	var wg sync.WaitGroup
	wg.Add(coroutineCount)
	for i := 0; i < coroutineCount; i++ {
		go func(i int) {
			defer wg.Done()
			runtime.Gosched()
			results[i] = i
		}(i)
	}
	wg.Wait()

	sum := 0
	for _, r := range results {
		sum += r
	}

	fmt.Println("coroutine-churn", sum)
}
