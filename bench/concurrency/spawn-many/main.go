package main

import (
	"fmt"
	"sync"

	"maxon.dev/bench/concurrency/internal/argv"
)

const defaultThreadCount = 100000

func main() {
	threadCount := argv.Size(1, defaultThreadCount)
	results := make([]int, threadCount)

	var wg sync.WaitGroup
	wg.Add(threadCount)
	for i := 0; i < threadCount; i++ {
		go func(i int) {
			defer wg.Done()
			results[i] = i * 2
		}(i)
	}
	wg.Wait()

	sum := 0
	for _, r := range results {
		sum += r
	}

	fmt.Println("spawn-many", sum)
}
