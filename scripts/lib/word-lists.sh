# SPACE-SEPARATED WORD LISTS — written ONCE, for every script that tests membership in one.

# Whether the space-separated list $1 holds the word $2.
list_has() {
	case " $1 " in
		*" $2 "*) return 0 ;;
		*) return 1 ;;
	esac
}
