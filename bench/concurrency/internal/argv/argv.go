package argv

import (
	"fmt"
	"os"
	"strconv"
)

func Size(position int, fallback int) int {
	if len(os.Args) <= position {
		return fallback
	}

	value, err := strconv.Atoi(os.Args[position])
	if err != nil || value < 0 {
		fmt.Fprintf(os.Stderr, "argument %d is %q, which is not a non-negative integer\n", position, os.Args[position])
		os.Exit(2)
	}

	return value
}
