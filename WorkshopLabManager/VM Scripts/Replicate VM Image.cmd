az sig image-version update -g sql-hol-rg --gallery-name vm_sql_hol_gallery --gallery-image-definition vm-sql-hol-master04-image --gallery-image-version 1.0.0 --target-regions "eastus" "eastus2" "southcentralus" "northcentralus" "centralus"
pause
